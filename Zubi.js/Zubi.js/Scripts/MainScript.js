(() => {
    'use strict';

    const NAME = 'Zubi.js';

    // ---- Token ----
    const meta = document.querySelector('meta[name="bridge-token"]');
    let token = meta?.content;

    if (!token && location.hash.startsWith('#token=')) {
        token = location.hash.slice('#token='.length);
        sessionStorage.setItem('bridge_token', token);
        history.replaceState(null, '', location.pathname + location.search);
    }

    if (!token)
        token = sessionStorage.getItem('bridge_token');

    if (!token) {
        console.error(`[${NAME}] Sin token. La app no se autenticará.`);
        window.native = new Proxy({}, {
            get: () => () => Promise.reject(new Error('Bridge not authenticated')),
        });
        return;
    }

    sessionStorage.setItem('bridge_token', token);

    // ---- WebSocket ----
    const wsUrl = window.__BRIDGE_WS_URL__ || `ws://${location.host}/ws?token=${encodeURIComponent(token)}`;

    const Instances = new Map();

    class NativeBridge {

        constructor(url) {
            this._url = url;
            this._ws = null;
            this._nextId = 1;
            this._pending = new Map();
            this._instanceEvents = new Map();
            this._subscribedEvents = new Map();
            this._connectPromise = this._connect();
        }

        _connect() {
            return new Promise((resolve, reject) => {
                const ws = new WebSocket(this._url);
                this._ws = ws;

                ws.addEventListener('open', () => {
                    console.log(`[${NAME}] Connected`);
                    resolve();
                });

                ws.addEventListener('message', (e) => this._onMessage(e));

                ws.addEventListener('error', () => {
                    if (ws.readyState !== WebSocket.OPEN) 
                        reject(new Error('WebSocket error'));
                });

                ws.addEventListener('close', () => {
                    console.warn(`[${NAME}] Disconnected`);
                    
                    for (const [, entry] of this._pending) 
                        entry.reject(new Error('Connection closed'));
                    
                    this._pending.clear();
                });
            });
        }

        _onMessage(event) {
            let msg;
            try { msg = JSON.parse(event.data); }
            catch { return; }

            if (msg.event) {
                if (msg.uid)
                    this._dispatchInstanceEvent(msg.uid, msg.event, msg.data);
                return;
            }

            const entry = this._pending.get(msg.id);

            if (!entry)
                return;

            if (msg.chunk !== undefined) {
                if (entry.onChunk) {
                    const raw = atob(msg.chunk);
                    const bytes = new Uint8Array(raw.length);
                    
                    for (let i = 0; i < raw.length; i++) 
                        bytes[i] = raw.charCodeAt(i);

                    entry.chain = entry.chain
                        .then(() => entry.onChunk(bytes))
                        .catch((err) => {
                            this._pending.delete(msg.id);
                            this._ws.send(JSON.stringify({ id: msg.id, cancel: true }));
                            entry.reject(err);
                        });
                }
                return;
            }

            this._pending.delete(msg.id);
            if (msg.end) { 
                entry.resolve(msg.result); 
                return; 
            }
            
            if (msg.error) {
                const err = new Error(msg.error.message || 'RPC error');
                err.code = msg.error.code;
                entry.reject(err);
                return;
            }
            entry.resolve(msg.result);
        }

        async _call(uid, method, args) {
            await this._connectPromise;

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

            const id = this._nextId++;
            return new Promise((resolve, reject) => {
                const entry = { 
                    resolve, 
                    reject, 
                    onChunk: opts?.onChunk,
                    chain: Promise.resolve(),
                };
                this._pending.set(id, entry);

                if (opts?.signal) {
                    if (opts.signal.aborted) {
                        this._pending.delete(id);
                        reject(new DOMException('Aborted', 'AbortError'));
                        return;
                    }
                    opts.signal.addEventListener('abort', () => {
                        this._pending.delete(id);
                        try { this._ws.send(JSON.stringify({ id, cancel: true })); } 
                        catch { }
                        reject(new DOMException('Aborted', 'AbortError'));
                    }, { once: true });
                }

                try {
                    this._ws.send(JSON.stringify({ uid, id, method, params: args }));
                } catch (e) {
                    this._pending.delete(id);
                    reject(e);
                }
            });
        }

        navigate(url) {
            const u = new URL(url);
            if (u.origin !== location.origin) 
                u.hash = `token=${token}`;
            
            location.href = u.toString();
        }

        _addInstanceListener(uid, type, listener, rawListener, options) {
            let events = this._instanceEvents.get(uid);
            if (!events) { 
                events = new Map(); 
                this._instanceEvents.set(uid, events); 
            }

            let handlers = events.get(type);
            if (!handlers) { 
                handlers = new Set(); 
                events.set(type, handlers); 
            }

            handlers.add({ listener, raw: rawListener, options });
        }

        _removeInstanceListener(uid, type, rawListener) {
            const events = this._instanceEvents.get(uid);
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

                const subs = this._subscribedEvents.get(uid);
                if (subs && subs.has(type)) {
                    subs.delete(type);
                    this._call('Plugins', 'UnsubscribeEvents', [uid, type]).catch(() => { });
                }
            }

            if (!events.size) this._instanceEvents.delete(uid);
        }

        _dispatchInstanceEvent(uid, type, detail) {
            const events = this._instanceEvents.get(uid);
            if (!events) 
                return;

            const handlers = events.get(type);
            if (!handlers || !handlers.size) 
                return;

            const ev = new CustomEvent(type, { detail });
            const self = Instances.get(uid) || null;

            for (const entry of [...handlers]) {
                try {
                    entry.listener.call(self, ev);
                    if (entry.options?.once) handlers.delete(entry);
                } catch (err) {
                    setTimeout(() => { throw err; });
                }
            }

            if (!handlers.size) 
                events.delete(type);
        }

        // Devuelve la promesa del SubscribeEvents (idempotente por uid+type)
        _ensureSubscribed(uid, type) {
            let subs = this._subscribedEvents.get(uid);
            if (!subs) { 
                subs = new Set(); 
                this._subscribedEvents.set(uid, subs); 
            }

            if (subs.has(type))
                return Promise.resolve();

            return this._call('Plugins', 'SubscribeEvents', [uid, type])
                .then(() => { subs.add(type); })
                .catch((err) => {
                    console.error(`[${NAME}] SubscribeEvents falló (${uid}:${type})`, err);
                    throw err;
                });
        }

        // Al Dispose del plugin: damos de baja todo en C# y limpiamos
        _clearInstanceEvents(uid) {
            const subs = this._subscribedEvents.get(uid);
            if (subs) {
                for (const type of [...subs]) {
                    this._call('Plugins', 'UnsubscribeEvents', [uid, type]).catch(() => { });
                }
                this._subscribedEvents.delete(uid);
            }
            this._instanceEvents.delete(uid);
        }
    }

    const bridge = new NativeBridge(wsUrl);

    function makePluginProxy(uid) {
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

                    case 'addEventListener':
                    case 'AddEventListener':
                        return (type, listener, options) => {
                            const cb = typeof listener === 'function'
                                ? listener
                                : (listener && typeof listener.handleEvent === 'function'
                                    ? listener.handleEvent.bind(listener)
                                    : null);

                            if (!cb) 
                                throw new TypeError('listener inválido');

                            bridge._addInstanceListener(uid, type, cb, listener, options || {});
                            return bridge._ensureSubscribed(uid, type);
                        };

                    case 'removeEventListener':
                    case 'RemoveEventListener':
                        return (type, listener) => {
                            bridge._removeInstanceListener(uid, type, listener);
                        };

                    case 'dispatchEvent':
                    case 'DispatchEvent':
                        return (event) => {
                            bridge._dispatchInstanceEvent(uid, event.type, event.detail);
                            return true;
                        };
                }

                const callable = (...args) => bridge._call(uid, method, args);
                callable.then = (onFulfilled, onRejected) => bridge._call(uid, method, []).then(onFulfilled, onRejected);

                // Dispose: ejecuta en C# y limpia en JS (eventos + Instances)
                if (method === 'Dispose') {
                    return async (...args) => {
                        try {
                            return await bridge._call(uid, method, args);
                        } finally {
                            bridge._clearInstanceEvents(uid);
                            Instances.delete(uid);
                        }
                    };
                }

                return callable;
            },
        });
    }

    const PluginsApi = {
        get Loaded(){
            return bridge._call('Plugins', 'Loaded', []);
        },
        async GetInstance(uid) {
            if(Instances.has(uid))
                return Instances.get(uid);

            await bridge._call('Plugins', 'GetInstance', [uid]);
            const instance = makePluginProxy(uid);
            Instances.set(uid, instance);
            return instance;
        },
        Load(pluginPath){
            return bridge._call('Plugins', 'Load', [pluginPath]);
        },
        LoadFrom(folderPath = null){
            return bridge._call('Plugins', 'LoadFrom', [folderPath]);
        },
        async NewInstance(pluginName, ...args){
            const result = await bridge._call('Plugins', 'NewInstance', [pluginName, args]);
            const uid = result.uid;

            if(Instances.has(uid))
                return Instances.get(uid);

            const instance = makePluginProxy(uid);
            Instances.set(uid, instance);
            return instance;
        },
        Unload(pluginName){
            return bridge._call('Plugins', 'Unload', [pluginName]);
        },
        Navigate(url){
            bridge.navigate(url);
        }
    }

    Object.defineProperty(window, NAME.split('.')[0], {
        get: () => PluginsApi
    });

    console.log(`[${NAME}] Ready`);
})();