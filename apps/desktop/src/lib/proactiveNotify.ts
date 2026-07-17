/**
 * System-notification delivery for proactive impulses (no quick-window popup).
 * On platforms that support toast actions (esp. mobile), users can reply from the notification.
 * On Windows desktop, clicking the toast focuses the main window; reply is completed there.
 */

export type ProactiveNotifyItem = {
  id: string;
  body: string;
  type?: string;
  reason?: string;
  origin?: 'ai' | 'system';
  chainId?: string;
  state?: string;
  ts?: number;
};

const ACTION_TYPE_ID = 'pattern-proactive';
let actionsReady = false;
let actionListenerBound = false;

function itemIdToNotificationId(itemId: string): number {
  let hash = 0;
  for (let i = 0; i < itemId.length; i++) hash = (hash * 31 + itemId.charCodeAt(i)) | 0;
  // keep in signed 32-bit positive range preferred by the plugin
  return Math.abs(hash) || 1;
}

async function ensureNotificationPermission(): Promise<boolean> {
  const {isPermissionGranted, requestPermission} = await import('@tauri-apps/plugin-notification');
  let granted = await isPermissionGranted();
  if (!granted) {
    const state = await requestPermission();
    granted = state === 'granted';
  }
  return granted;
}

async function ensureActionTypes() {
  if (actionsReady) return;
  const {registerActionTypes} = await import('@tauri-apps/plugin-notification');
  try {
    await registerActionTypes([
      {
        id: ACTION_TYPE_ID,
        actions: [
          {
            id: 'reply',
            title: '回复',
            input: true,
            inputButtonTitle: '发送',
            inputPlaceholder: '输入回复…',
            foreground: true,
          },
          {id: 'open', title: '打开对话', foreground: true},
          {id: 'dismiss', title: '忽略', foreground: false},
        ],
      },
    ]);
    actionsReady = true;
  } catch {
    // Desktop Windows may not support action types; still send plain toasts.
    actionsReady = true;
  }
}

export async function bindProactiveNotificationActions(handlers: {
  onOpen: (item: ProactiveNotifyItem) => void | Promise<void>;
  onReply: (item: ProactiveNotifyItem, text: string) => void | Promise<void>;
  onDismiss: (item: ProactiveNotifyItem) => void | Promise<void>;
}): Promise<void> {
  if (!(window as any).__TAURI_INTERNALS__ || actionListenerBound) return;
  actionListenerBound = true;
  try {
    await ensureActionTypes();
    const {onAction} = await import('@tauri-apps/plugin-notification');
    await onAction(async (notification: any) => {
      const extra = (notification?.extra || {}) as Record<string, unknown>;
      const item: ProactiveNotifyItem = {
        id: String(extra.itemId || notification?.extra?.itemId || ''),
        body: String(extra.body || notification?.body || ''),
        type: extra.type ? String(extra.type) : undefined,
        reason: extra.reason ? String(extra.reason) : undefined,
        origin: (extra.origin as 'ai' | 'system' | undefined) || 'ai',
        chainId: extra.chainId ? String(extra.chainId) : undefined,
      };
      if (!item.id) return;
      const actionId = String(notification?.actionId || extra.actionId || 'open');
      const inputText = String(
        notification?.userText
          || notification?.inputValue
          || notification?.input
          || extra.userText
          || extra.input
          || '',
      ).trim();
      try {
        const {invoke} = await import('@tauri-apps/api/core');
        await invoke('show_main');
      } catch {
        /* main may already be focused */
      }
      if (actionId === 'dismiss') {
        await handlers.onDismiss(item);
        return;
      }
      if (actionId === 'reply' && inputText) {
        await handlers.onReply(item, inputText);
        return;
      }
      await handlers.onOpen(item);
    });
  } catch {
    /* plugin optional in browser demo */
  }
}

export async function showProactiveSystemNotification(item: ProactiveNotifyItem): Promise<boolean> {
  if (!(window as any).__TAURI_INTERNALS__) return false;
  try {
    const granted = await ensureNotificationPermission();
    if (!granted) return false;
    await ensureActionTypes();
    const {sendNotification} = await import('@tauri-apps/plugin-notification');
    const title = item.origin === 'system' ? 'Pattern · 系统提醒' : 'Pattern · 主动消息';
    const body = (item.body || '').trim() || '有一条新的主动消息';
    sendNotification({
      id: itemIdToNotificationId(item.id),
      title,
      body: body.slice(0, 180),
      largeBody: body.slice(0, 2000),
      summary: '点击通知可在主窗口回复',
      actionTypeId: ACTION_TYPE_ID,
      autoCancel: true,
      extra: {
        itemId: item.id,
        body: item.body,
        type: item.type || '',
        reason: item.reason || '',
        origin: item.origin || 'ai',
        chainId: item.chainId || '',
      },
    });
    return true;
  } catch (error) {
    console.warn('[pattern] proactive system notification failed', error);
    return false;
  }
}
