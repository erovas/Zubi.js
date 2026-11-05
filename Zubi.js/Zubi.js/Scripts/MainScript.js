(() => {
    'use strict';

    const NAME = 'Zubi.js';
    const UID = '___UID___';

    // ---- Token ----
    const meta = document.querySelector('meta[name="bridge-token"]');
    let TOKEN = meta?.content;

    if (!TOKEN && location.hash.startsWith('#token=')) {
        TOKEN = location.hash.slice('#token='.length);
        sessionStorage.setItem('bridge_token', TOKEN);
        history.replaceState(null, '', location.pathname + location.search);
    }

    if (!TOKEN)
        TOKEN = sessionStorage.getItem('bridge_token');

    if (!TOKEN) {
        console.error(`[${NAME}] without token.`);
        return;
    }

    sessionStorage.setItem('bridge_token', TOKEN);

    // ---- WebSocket ----
    const wsUrl = window.__BRIDGE_WS_URL__ || `ws://${location.host}/ws?token=${encodeURIComponent(TOKEN)}`;

    /**
     * Map<"UID Instance", Proxy>> 
     * @type {Map<string, Proxy>} 
    */
    const PluginInstances = new Map();

    class NativeBridge {

        /** @type {string} */
        #url;
        /** @type {WebSocket} */
        #ws;
        /** @type {Number} */
        #nextId;
        /** @type {Map<Number, Promise>} */
        #pending;
        /**
         * Map<"UID Instance", Map<"EventName", Set<{...}>>> 
         * @type {Map<string, Map<string, Set<{ listener: Function, raw: Function, options: Any }>>>} 
        */
        #instanceEvents;
        /**
         * Map<"UID Instance", Set<"EventName">> 
         * @type {Map<string, Set<string>>} 
        */
        #subscribedEvents;
        /** @type {Promise} */
        #connectPromise;

        /**
         * 
         * @param {string} url 
         */
        constructor(url) {
            this.#url = url;
            this.#nextId = 1;
            this.#pending = new Map();
            this.#instanceEvents = new Map();
            this.#subscribedEvents = new Map();
            this.#connectPromise = this.#connect();
        }

        #connect() {
            return new Promise((resolve, reject) => {
                const ws = new WebSocket(this.#url);
                this.#ws = ws;

                ws.addEventListener('open', () => {
                    console.log(`[${NAME}] Connected`);
                    resolve();
                });

                ws.addEventListener('message', (e) => this.#onMessage(e));

                ws.addEventListener('error', () => {
                    if (ws.readyState !== WebSocket.OPEN) 
                        reject(new Error('WebSocket error'));
                });

                ws.addEventListener('close', () => {
                    console.warn(`[${NAME}] Disconnected`);
                    
                    for (const [, entry] of this.#pending) 
                        entry.reject(new Error('Connection closed'));
                    
                    this.#pending.clear();
                });
            });
        }

        #onMessage(event) {
            let msg;
            try { msg = JSON.parse(event.data); }
            catch { return; }

            if(msg.LiveReload)
                location.reload();

            // Disparo de evento de C# para JS
            if (msg.event) {
                if (msg.uid)
                    this.DispatchInstanceEvent(msg.uid, msg.event, this.#convertValue(msg.data));
                return;
            }

            const entry = this.#pending.get(msg.id);

            if (!entry)
                return;

            // Descarga de datos por trozos
            if (msg.chunk !== undefined) {
                if (entry.onChunk) {
                    const raw = atob(msg.chunk);
                    const bytes = new Uint8Array(raw.length);
                    
                    for (let i = 0; i < raw.length; i++) 
                        bytes[i] = raw.charCodeAt(i);

                    entry.chain = entry.chain
                        .then(() => entry.onChunk(bytes))
                        .catch((err) => {
                            this.#pending.delete(msg.id);
                            this.#ws.send(JSON.stringify({ id: msg.id, cancel: true }));
                            entry.reject(err);
                        });
                }
                return;
            }

            // Eliminar registro tarea (llamada de metodo/propiedad C# desde JS)
            this.#pending.delete(msg.id);

            // Fin de descarga de datos por trozos
            if (msg.end) { 
                entry.resolve(msg.result); 
                return; 
            }
            
            // Error al llamar metodo/propiedad
            if (msg.error) {
                const err = new Error(msg.error.message || 'RPC error');
                const uid = msg.error.uid;
                err.code = msg.error.code;
                // Es una instancia ya liberada
                if(uid && err.code === 'EOBJDISP' && PluginInstances.has(uid)){
                    this.ClearInstanceEvents(uid);
                    PluginInstances.delete(uid);
                }
                entry.reject(err);
                return;
            }

            // Valor de retorno C#, OK
            entry.resolve(this.#convertValue(msg.result));
        }

        /**
         * Varificar si lo que viene de C# es un plugin, retornar el Proxy si lo es.
         * @param {any} obj 
         * @returns 
         */
        #convertValue(obj){
            if(obj && obj[UID]){
                const uid = obj[UID];

                if(PluginInstances.has(uid))
                    return PluginInstances.get(uid);
                else
                    return MakePluginProxy(uid);
            }

            return obj;
        }

        /**
         * Llamar a un método/getter/setter de una instancia de una clase en C#
         * @param {string} uid - UID de la instancia del plugin.
         * @param {string} method - Nombre método/getter/setter del plugin que se quiere llamar.
         * @param {object[]} args - Parametros que se le pasan al método/getter/setter.
         * @returns 
         */
        async Call(uid, method, args) {
            await this.#connectPromise;

            // Último argumento puede ser opts: { signal, onChunk }
            let opts = null;
            if (args.length > 0) {
                const last = args[args.length - 1];
                if (last && typeof last === 'object' && !Array.isArray(last)
                    && ('signal' in last || 'onChunk' in last)) {
                    opts = last;
                    args = args.slice(0, -1);
                }
            }

            const id = this.#nextId++;
            return new Promise((resolve, reject) => {
                const entry = { 
                    resolve, 
                    reject, 
                    onChunk: opts?.onChunk,
                    chain: Promise.resolve(),
                };
                this.#pending.set(id, entry);

                if (opts?.signal) {
                    if (opts.signal.aborted) {
                        this.#pending.delete(id);
                        reject(new DOMException('Aborted', 'AbortError'));
                        return;
                    }
                    opts.signal.addEventListener('abort', () => {
                        this.#pending.delete(id);
                        try { this.#ws.send(JSON.stringify({ id, cancel: true })); } 
                        catch { }
                        reject(new DOMException('Aborted', 'AbortError'));
                    }, { once: true });
                }

                try {
                    this.#ws.send(JSON.stringify({ uid, id, method, params: args }));
                } catch (e) {
                    this.#pending.delete(id);
                    reject(e);
                }
            });
        }

        /**
         * 
         * @param {string} uid - UID de la instancia del plugin.
         * @param {string} type - Nombre del evento.
         * @param {Function} listener - Manejador JS enlazado.
         * @param {Function} rawListener - Manejador JS original.
         * @param {*} options 
         */
        AddInstanceListener(uid, type, listener, rawListener, options) {
            let events = this.#instanceEvents.get(uid);
            if (!events) { 
                events = new Map(); 
                this.#instanceEvents.set(uid, events); 
            }

            let handlers = events.get(type);
            if (!handlers) { 
                handlers = new Set(); 
                events.set(type, handlers); 
            }

            handlers.add({ listener, raw: rawListener, options });
        }

        /**
         * 
         * @param {string} uid - UID de la instancia del plugin.
         * @param {string} type - Nombre del evento.
         * @param {Function} rawListener - Manejador JS original del evento.
         * @returns 
         */
        RemoveInstanceListener(uid, type, rawListener) {
            const events = this.#instanceEvents.get(uid);
            if (!events) 
                return;

            const handlers = events.get(type);
            if (!handlers) 
                return;

            for (const entry of [...handlers]) {
                if (entry.raw === rawListener)
                    handlers.delete(entry);
            }

            // Si ya no queda nadie escuchando este evento, damos de baja en C#
            if (!handlers.size) {
                events.delete(type);

                const subs = this.#subscribedEvents.get(uid);
                if (subs && subs.has(type)) {
                    subs.delete(type);
                    this.Call('Plugins', 'UnsubscribeEvents', [uid, type]).catch(() => { });
                }
            }

            if (!events.size) this.#instanceEvents.delete(uid);
        }

        /**
         * 
         * @param {string} uid - UID de la instancia del plugin.
         * @param {string} type - Nombre del evento.
         * @param {*} detail 
         * @returns 
         */
        DispatchInstanceEvent(uid, type, detail) {
            const events = this.#instanceEvents.get(uid);
            if (!events) 
                return;

            const handlers = events.get(type);
            if (!handlers || !handlers.size) 
                return;

            const ev = new CustomEvent(type, { detail });
            const self = PluginInstances.get(uid) || null;

            for (const entry of [...handlers]) {
                try {
                    entry.listener.call(self, ev);
                    if (entry.options?.once) 
                        handlers.delete(entry);
                } catch (err) {
                    setTimeout(() => { throw err; });
                }
            }

            if (!handlers.size) 
                events.delete(type);
        }

        /**
         * Devuelve la promesa del SubscribeEvents (idempotente por uid+type)
         * @param {string} uid - UID Plugin instance
         * @param {string} type - Event name
         * @returns 
         */
        EnsureSubscribed(uid, type) {
            let subs = this.#subscribedEvents.get(uid);
            if (!subs) { 
                subs = new Set(); 
                this.#subscribedEvents.set(uid, subs); 
            }

            if (subs.has(type))
                return Promise.resolve();

            return this.Call('Plugins', 'SubscribeEvents', [uid, type])
                .then(() => { subs.add(type); })
                .catch((err) => {
                    console.error(`[${NAME}] SubscribeEvents falló (${uid}:${type})`, err);
                    throw err;
                });
        }

        /**
         * Al hacer Dispose del plugin, se da de baja todo en C# y limpiamos
         * @param {string} uid 
         */
        ClearInstanceEvents(uid) {
            const subs = this.#subscribedEvents.get(uid);
            if (subs) {
                for (const type of [...subs]) {
                    this.Call('Plugins', 'UnsubscribeEvents', [uid, type]).catch(() => { });
                }
                this.#subscribedEvents.delete(uid);
            }
            this.#instanceEvents.delete(uid);
        }

    }

    const Bridge = new NativeBridge(wsUrl);

    /**
     * 
     * @param {string} uid 
     * @returns 
     */
    function MakePluginProxy(uid) {
        return new Proxy({}, {
            get(_, method) {

                if (typeof method !== 'string') 
                    return undefined;

                switch (method) {
                    case 'then':
                    case 'catch':
                    case 'finally':
                        return undefined;
                
                    case 'UID':
                        return uid;

                    case 'AddEventListener':
                        return (type, listener, options) => {
                            const cb = typeof listener === 'function'
                                ? listener
                                : (listener && typeof listener.handleEvent === 'function'
                                    ? listener.handleEvent.bind(listener)
                                    : null);

                            if (!cb) 
                                throw new TypeError('listener inválido');

                            Bridge.AddInstanceListener(uid, type, cb, listener, options || {});
                            return Bridge.EnsureSubscribed(uid, type);
                        };

                    case 'RemoveEventListener':
                        return (type, listener) => {
                            Bridge.RemoveInstanceListener(uid, type, listener);
                        };

                    case 'DispatchEvent':
                        return (event) => {
                            Bridge.DispatchInstanceEvent(uid, event.type, event.detail);
                            return true;
                        };
                    case 'Dispose':
                        return async (...args) => {
                            try {
                                return await Bridge.Call(uid, method, args);
                            } finally {
                                Bridge.ClearInstanceEvents(uid);
                                PluginInstances.delete(uid);
                            }
                        };
                    case 'toJSON':
                        return () => ({ [UID]: uid });
                }

                const callable = (...args) => Bridge.Call(uid, method, args);
                callable.then = (onFulfilled, onRejected) => Bridge.Call(uid, method, []).then(onFulfilled, onRejected);

                return callable;
            },
        });
    }

    const PluginsApi = {
        get Loaded(){
            return Bridge.Call('Plugins', 'Loaded', []);
        },
        async GetInstance(uid) {
            if(PluginInstances.has(uid))
                return PluginInstances.get(uid);

            await Bridge.Call('Plugins', 'GetInstance', [uid]);
            const instance = MakePluginProxy(uid);
            PluginInstances.set(uid, instance);
            return instance;
        },
        Load(pluginPath){
            return Bridge.Call('Plugins', 'Load', [pluginPath]);
        },
        LoadFrom(folderPath = null){
            return Bridge.Call('Plugins', 'LoadFrom', [folderPath]);
        },
        NewInstance(pluginName, ...args){
           return Bridge.Call('Plugins', 'NewInstance', [pluginName, args]);
        },
        Unload(pluginName){
            return Bridge.Call('Plugins', 'Unload', [pluginName]);
        },
        Navigate(url){
            const u = new URL(url);
            if (u.origin !== location.origin) 
                u.hash = `token=${TOKEN}`;
            
            location.href = u.toString();
        }
    }

    Object.defineProperty(window, NAME.split('.')[0], {
        get: () => PluginsApi
    });

    console.log(`[${NAME}] Ready`);
})();

/*

(async _ =>{
    const Plugins = window.Zubi
    const results = await Plugins.LoadFrom();
    console.log(results);
    const FS = await Plugins.NewInstance('FS');
    console.log(await FS.UID);
    console.log(await FS.Exists(`C:/Users/Manurov/Documents`))
    const ticker = await FS.Ticker;
    // Suscripción (el await es opcional; el handler ya está registrado síncronamente)
    ticker.AddEventListener("Tick", (e) => {
        console.log(`Tick #${e.detail.Count} a las ${e.detail.Timestamp}`);
    });

    // Una sola vez
    ticker.AddEventListener("Started", (e) => {
        console.log("Ticker arrancado");
    }, { once: true });

    // Alias compatible con tu ejemplo
    await ticker.AddEventListener("Stopped", (e) => {
        console.log("Ticker detenido");
    });

    await ticker.Start(500);

    setTimeout(_ => {
        FS.Dispose();
        console.log("FS Disposed")
    }, 5000)
})();

*/

