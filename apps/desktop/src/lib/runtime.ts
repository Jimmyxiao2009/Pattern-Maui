import type {ClientMessage, RuntimeConnection, ServerMessage} from '@pattern/protocol';

type Handler = (message: ServerMessage) => void;
type StatusHandler = (connected: boolean) => void;

function browserRuntimeHint(): RuntimeConnection | null {
  const params = new URLSearchParams(location.search);
  const port = Number(params.get('runtimePort') || localStorage.getItem('pattern-runtime-port') || '');
  const token = params.get('runtimeToken') || localStorage.getItem('pattern-runtime-token') || '';
  if (!port || !token) return null;
  return {port, token};
}

function buildWsUrl(connection: RuntimeConnection): string {
  const token = encodeURIComponent(connection.token);
  return 'ws://127.0.0.1:' + connection.port + '/ws?token=' + token;
}

class RuntimeClient {
  private socket?: WebSocket;
  private connecting?: Promise<boolean>;
  private pending = new Map<string, {resolve: (message: ServerMessage) => void; reject: (error: Error) => void}>();
  private listeners = new Set<Handler>();
  private statusListeners = new Set<StatusHandler>();
  private reconnectTimer?: ReturnType<typeof setTimeout>;
  private reconnectAttempt = 0;
  private intentionalClose = false;

  get connected() {
    return this.socket?.readyState === WebSocket.OPEN;
  }

  on(handler: Handler) {
    this.listeners.add(handler);
    return () => {
      this.listeners.delete(handler);
    };
  }

  onStatus(handler: StatusHandler) {
    this.statusListeners.add(handler);
    handler(this.connected);
    return () => {
      this.statusListeners.delete(handler);
    };
  }

  private setConnected(value: boolean) {
    for (const listener of this.statusListeners) listener(value);
  }

  async connect(): Promise<boolean> {
    if (this.connected) return true;
    if (this.connecting) return this.connecting;
    this.connecting = this.open();
    const result = await this.connecting;
    this.connecting = undefined;
    if (result) {
      this.reconnectAttempt = 0;
      this.setConnected(true);
    } else {
      this.scheduleReconnect();
    }
    return result;
  }

  async ensureConnected(retries = 2): Promise<boolean> {
    for (let attempt = 0; attempt <= retries; attempt++) {
      if (await this.connect()) return true;
      await new Promise((resolve) => setTimeout(resolve, 250 * (attempt + 1)));
    }
    return this.connected;
  }

  private scheduleReconnect() {
    if (this.intentionalClose || this.reconnectTimer) return;
    const delay = Math.min(1000 * 2 ** this.reconnectAttempt, 15_000);
    this.reconnectAttempt += 1;
    this.reconnectTimer = setTimeout(() => {
      this.reconnectTimer = undefined;
      void this.connect();
    }, delay);
  }

  private async resolveConnection(): Promise<RuntimeConnection | null> {
    if ((window as any).__TAURI_INTERNALS__) {
      try {
        const {invoke} = await import('@tauri-apps/api/core');
        return await invoke<RuntimeConnection | null>('runtime_connection');
      } catch (error) {
        console.error('[pattern] runtime_connection failed', error);
        return null;
      }
    }
    return browserRuntimeHint();
  }

  private async open(): Promise<boolean> {
    try {
      const connection = await this.resolveConnection();
      if (!connection) return false;
      return await new Promise<boolean>((resolve) => {
        const socket = new WebSocket(buildWsUrl(connection));
        const timer = setTimeout(() => {
          socket.close();
          resolve(false);
        }, 4000);
        socket.onopen = () => {
          clearTimeout(timer);
          this.socket = socket;
          resolve(true);
        };
        socket.onerror = () => {
          clearTimeout(timer);
          resolve(false);
        };
        socket.onclose = () => {
          if (this.socket === socket) this.socket = undefined;
          this.setConnected(false);
          for (const [id, pending] of this.pending) {
            this.pending.delete(id);
            pending.reject(new Error('Agent 运行时连接已断开'));
          }
          if (!this.intentionalClose) this.scheduleReconnect();
        };
        socket.onmessage = (event) => this.receive(JSON.parse(event.data) as ServerMessage);
      });
    } catch (error) {
      console.error('[pattern] runtime connection failed', error);
      return false;
    }
  }

  private receive(message: ServerMessage) {
    for (const listener of this.listeners) listener(message);
    if ('id' in message && typeof (message as any).id === 'string') {
      const id = (message as any).id as string;
      // chat stream messages are handled by listeners only
      if (message.type === 'chat.delta' || message.type === 'chat.started') return;
      const pending = this.pending.get(id);
      if (pending) {
        this.pending.delete(id);
        pending.resolve(message);
      }
    }
  }

  async request<T extends ServerMessage>(message: ClientMessage, timeoutMs = 20000): Promise<T> {
    if (!(await this.ensureConnected()) || !this.socket) throw new Error('Agent 运行时未连接');
    const id = 'id' in message ? String((message as any).id) : crypto.randomUUID();
    return await new Promise<T>((resolve, reject) => {
      const timer = setTimeout(() => {
        this.pending.delete(id);
        reject(new Error('运行时请求超时'));
      }, timeoutMs);
      this.pending.set(id, {
        resolve: (value) => {
          clearTimeout(timer);
          resolve(value as T);
        },
        reject,
      });
      this.socket!.send(JSON.stringify(message));
    });
  }

  private chatHandlers = new Map<string, Handler>();

  async chat(
    message: Extract<ClientMessage, {type: 'chat.send'}>,
    callbacks: {
      onDelta: (delta: string) => void;
      onDone: () => void;
      onError: (message: string) => void;
      onEvent?: (event: {kind: string; text: string; ts?: number}) => void;
    },
  ) {
    if (!(await this.ensureConnected()) || !this.socket) throw new Error('Agent 运行时未连接');
    let aborted = false;
    const handler: Handler = (msg) => {
      if (!('id' in msg) || (msg as any).id !== message.id) return;
      if (aborted) return;
      if (msg.type === 'chat.delta') callbacks.onDelta(msg.delta);
      if (msg.type === 'chat.event') callbacks.onEvent?.(msg.event);
      if (msg.type === 'chat.done') {
        this.listeners.delete(handler);
        this.chatHandlers.delete(message.id);
        callbacks.onDone();
      }
      if (msg.type === 'chat.error') {
        this.listeners.delete(handler);
        this.chatHandlers.delete(message.id);
        callbacks.onError(msg.message);
      }
    };
    (handler as any).__abort = () => {
      aborted = true;
      this.listeners.delete(handler);
      this.chatHandlers.delete(message.id);
    };
    this.listeners.add(handler);
    this.chatHandlers.set(message.id, handler);
    this.socket.send(JSON.stringify(message));
  }

  abortChat(id: string) {
    const handler = this.chatHandlers.get(id) as any;
    if (handler?.__abort) handler.__abort();
  }
}

export const runtime = new RuntimeClient();

export function formatRuntimeError(error: unknown, options: {isDemo?: boolean; isTauri?: boolean} = {}): string {
  const text = error instanceof Error ? error.message : String(error || '未知错误');
  if (options.isDemo && /未连接|不可用/.test(text)) {
    return '演示模式：消息已记录。配置模型并启动桌面端后可获得完整回复。';
  }
  if (!options.isTauri && /未连接|不可用/.test(text)) {
    return 'Agent 运行时未连接。请使用 `pnpm tauri dev` 启动桌面端，或在地址栏附加 runtimePort/runtimeToken。';
  }
  if (/未连接|已断开/.test(text)) {
    return 'Agent 运行时暂时不可用，正在后台重连。请稍后重试，或检查模型配置与 sidecar。';
  }
  return text.replace(/^Error:\s*/, '');
}
