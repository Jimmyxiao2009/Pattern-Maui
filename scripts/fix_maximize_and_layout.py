from pathlib import Path

# Fix maximize + hide recents on project page
app = Path("apps/desktop/src/App.svelte")
t = app.read_text(encoding="utf-8")

t = t.replace(
    "  const showRecents = $derived(activeView === 'chat' || activeView === 'project' || activeView === 'conversations');",
    "  const showRecents = $derived(activeView === 'chat' || activeView === 'conversations');",
)

old = """  function windowAction(action: 'minimize' | 'maximize' | 'close') {
    if (!(window as any).__TAURI_INTERNALS__) {
      notify('桌面窗口操作将在 Tauri 中生效');
      return;
    }
    import('@tauri-apps/api/window').then(({getCurrentWindow}) => (getCurrentWindow() as any)[action]());
  }
"""
new = """  async function windowAction(action: 'minimize' | 'maximize' | 'close') {
    if (!(window as any).__TAURI_INTERNALS__) {
      notify('桌面窗口操作将在 Tauri 中生效');
      return;
    }
    try {
      const {getCurrentWindow} = await import('@tauri-apps/api/window');
      const win = getCurrentWindow();
      if (action === 'maximize') {
        // Custom titlebar: toggleMaximize is the correct API.
        await win.toggleMaximize();
        return;
      }
      if (action === 'minimize') {
        await win.minimize();
        return;
      }
      if (action === 'close') {
        await win.close();
      }
    } catch (error) {
      notify(`窗口操作失败：${error}`);
    }
  }
"""
if old not in t:
    raise SystemExit("windowAction missing")
t = t.replace(old, new, 1)
app.write_text(t, encoding="utf-8")
print("App maximize + recents fixed")

# capabilities
cap = Path("apps/desktop/src-tauri/capabilities/default.json")
c = cap.read_text(encoding="utf-8")
for perm in [
    "core:window:allow-maximize",
    "core:window:allow-unmaximize",
    "core:window:allow-is-maximized",
]:
    if perm not in c:
        c = c.replace(
            '"core:window:allow-toggle-maximize"',
            f'"core:window:allow-toggle-maximize", "{perm}"',
            1,
        )
cap.write_text(c, encoding="utf-8")
print("capabilities ok")

# CSS layout
css = Path("apps/desktop/src/app.css")
s = css.read_text(encoding="utf-8")

s = s.replace(
    ".recents-sidebar{width:250px;min-width:220px;display:flex;flex-direction:column;gap:18px;padding:14px 12px;border-right:1px solid var(--line);background:var(--surface);overflow:auto}",
    ".recents-sidebar{width:240px;min-width:200px;max-width:280px;display:flex;flex-direction:column;gap:18px;padding:14px 12px;border-right:1px solid var(--line);background:var(--surface);overflow:auto}",
)
s = s.replace(
    ".project-workspace{min-width:0;flex:1;display:grid;grid-template-columns:220px minmax(0,1fr) 260px;background:var(--surface)}",
    ".project-workspace{min-width:0;flex:1;min-height:0;display:grid;grid-template-columns:minmax(180px,220px) minmax(0,1fr) minmax(200px,260px);background:var(--surface)}",
)
s = s.replace(
    ".project-sidebar,.file-tree-pane{min-width:0;display:flex;flex-direction:column;background:var(--surface);overflow:auto}",
    ".project-sidebar,.file-tree-pane{min-width:0;min-height:0;display:flex;flex-direction:column;background:var(--surface);overflow:auto}",
)
s = s.replace(
    ".project-sidebar-head,.file-tree-head{display:flex;align-items:flex-start;justify-content:space-between;gap:10px;padding:18px 16px 12px;border-bottom:1px solid var(--line)}",
    ".project-sidebar-head,.file-tree-head{display:flex;align-items:flex-start;justify-content:space-between;gap:10px;padding:14px 12px 10px;border-bottom:1px solid var(--line)}",
)
s = s.replace(
    ".project-sidebar-head h1{margin:0;font-size:18px}",
    ".project-sidebar-head h1{margin:0;font-size:16px}",
)
s = s.replace(
    ".project-sidebar-head small,.file-tree-head strong{display:block;margin-top:4px;color:var(--faint);font-size:11px;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;max-width:150px}",
    ".project-sidebar-head small,.file-tree-head strong{display:block;margin-top:4px;color:var(--faint);font-size:11px;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;max-width:100%}",
)
s = s.replace(
    ".project-chat-list{padding:10px;display:flex;flex-direction:column;gap:4px;overflow:auto}",
    ".project-chat-list{padding:8px;display:flex;flex-direction:column;gap:4px;overflow:auto;min-height:0}",
)
s = s.replace(
    ".project-chat{min-width:0;display:flex;flex-direction:column}",
    ".project-chat{min-width:0;min-height:0;display:flex;flex-direction:column;overflow:hidden}",
)
s = s.replace(
    ".project-chat .composer{margin-left:18px;margin-right:18px}",
    ".project-chat .composer{margin:8px 16px 12px}",
)
s = s.replace(
    ".project-chat .chat-stream{padding-left:24px;padding-right:24px}",
    ".project-chat .chat-stream{flex:1;min-height:0;padding:10px 20px}",
)

# media queries
old_mq = """@media(max-width:1100px){
  .recents-sidebar{width:210px;min-width:190px}
  .project-workspace{grid-template-columns:190px minmax(0,1fr) 220px}
}
@media(max-width:900px){
  .recents-sidebar{display:none}
  .project-workspace{grid-template-columns:minmax(0,1fr)}
  .project-sidebar,.file-tree-pane{display:none}
}
"""
new_mq = """@media(max-width:1280px){
  .recents-sidebar{width:210px;min-width:180px}
  .project-workspace{grid-template-columns:minmax(160px,200px) minmax(0,1fr) minmax(180px,230px)}
}
@media(max-width:1100px){
  .recents-sidebar{width:190px;min-width:170px}
  .project-workspace{grid-template-columns:minmax(150px,180px) minmax(0,1fr) minmax(170px,210px)}
}
@media(max-width:980px){
  .project-workspace{grid-template-columns:minmax(0,1fr) minmax(180px,240px)}
  .project-sidebar{display:none}
}
@media(max-width:860px){
  .recents-sidebar{display:none}
  .project-workspace{grid-template-columns:minmax(0,1fr)}
  .project-sidebar,.file-tree-pane{display:none}
}
"""
if old_mq in s:
    s = s.replace(old_mq, new_mq, 1)
    print("media queries")
else:
    print("WARN media queries not replaced")

# extra project head styles
if ".project-chat .conversation-head" not in s:
    s += """
.project-chat .conversation-head{padding:16px 20px 10px;gap:12px;align-items:flex-start}
.project-chat .conversation-head h1{font-size:20px}
.project-head-actions{display:flex;gap:8px;flex-wrap:wrap;justify-content:flex-end}
.project-diff-panel{max-height:140px}
.file-preview-panel{max-height:160px;margin:0 16px 12px}
"""
    print("extra project styles")

css.write_text(s, encoding="utf-8")
print("css done")

# larger default window
conf = Path("apps/desktop/src-tauri/tauri.conf.json")
cj = conf.read_text(encoding="utf-8")
old_size = '"width": 1120,\n        "height": 760,\n        "minWidth": 900,\n        "minHeight": 600,'
new_size = '"width": 1280,\n        "height": 820,\n        "minWidth": 980,\n        "minHeight": 640,'
if old_size in cj:
    conf.write_text(cj.replace(old_size, new_size, 1), encoding="utf-8")
    print("window size")
else:
    # already changed?
    print("window size maybe already set")
    print("1120" in cj, "1280" in cj)
