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
            this._eventHandlers = new Map();
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
                const handlers = this._eventHandlers.get(msg.event);
                if (handlers)
                    for (const h of handlers) {
                        try { h(msg.data); }
                        catch { }
                    }
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

        on(event, handler) {
            if (!this._eventHandlers.has(event)) 
                this._eventHandlers.set(event, new Set());

            this._eventHandlers.get(event).add(handler);
            return () => this._eventHandlers.get(event)?.delete(handler);
        }

        navigate(url) {
            const u = new URL(url);
            if (u.origin !== location.origin) 
                u.hash = `token=${token}`;
            
            location.href = u.toString();
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
                }

                const callable = (...args) => bridge._call(uid, method, args);
                callable.then = (onFulfilled, onRejected) => bridge._call(uid, method, []).then(onFulfilled, onRejected);

                if(method == 'Dispose')
                    Instances.delete(uid);

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
            return makePluginProxy(uid);
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
        }
    }

    Object.defineProperty(window, NAME.split('.')[0], {
        get: () => PluginsApi
    });

    console.log(`[${NAME}] Ready`);
})();