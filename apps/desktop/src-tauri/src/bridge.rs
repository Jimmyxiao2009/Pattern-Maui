use base64::{engine::general_purpose::STANDARD as B64, Engine};
use serde::Serialize;
use serde_json::json;
use std::{
    fs,
    io::Cursor,
    path::PathBuf,
    sync::{
        atomic::{AtomicBool, Ordering},
        Arc, Mutex,
    },
    thread,
    time::{SystemTime, UNIX_EPOCH},
};
use tiny_http::{Header, Method, Response, Server, StatusCode};

#[derive(Clone)]
pub struct BridgeHandle {
    pub url: String,
    pub token: String,
    pub freeze: Arc<AtomicBool>,
}

#[derive(Serialize)]
struct IdleResponse {
    #[serde(rename = "idleSeconds")]
    idle_seconds: u64,
}

fn now_secs() -> u64 {
    SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .map(|d| d.as_secs())
        .unwrap_or(0)
}

fn journal_dir() -> PathBuf {
    dirs::data_local_dir()
        .unwrap_or_else(|| PathBuf::from("."))
        .join("pattern")
        .join("journal")
}

fn unauthorized() -> Response<Cursor<Vec<u8>>> {
    Response::from_string("unauthorized")
        .with_status_code(StatusCode(401))
}

fn json_response(value: serde_json::Value) -> Response<Cursor<Vec<u8>>> {
    Response::from_string(value.to_string())
        .with_header(Header::from_bytes(&b"Content-Type"[..], &b"application/json"[..]).unwrap())
}

fn read_body(request: &mut tiny_http::Request) -> String {
    let mut bytes = Vec::new();
    let reader = request.as_reader();
    let mut chunk = [0u8; 4096];
    loop {
        match std::io::Read::read(reader, &mut chunk) {
            Ok(0) => break,
            Ok(n) => bytes.extend_from_slice(&chunk[..n]),
            Err(_) => break,
        }
    }
    String::from_utf8_lossy(&bytes).into_owned()
}

fn check_auth(request: &tiny_http::Request, token: &str) -> bool {
    request
        .headers()
        .iter()
        .find(|h| h.field.equiv("Authorization"))
        .map(|h| h.value.as_str() == format!("Bearer {token}"))
        .unwrap_or(false)
}

#[cfg(windows)]
fn idle_seconds() -> u64 {
    use windows_sys::Win32::UI::Input::KeyboardAndMouse::{GetLastInputInfo, LASTINPUTINFO};
    unsafe {
        let mut info = LASTINPUTINFO {
            cbSize: std::mem::size_of::<LASTINPUTINFO>() as u32,
            dwTime: 0,
        };
        if GetLastInputInfo(&mut info) == 0 {
            return 0;
        }
        let tick = windows_sys::Win32::System::SystemInformation::GetTickCount();
        tick.saturating_sub(info.dwTime) as u64 / 1000
    }
}

#[cfg(not(windows))]
fn idle_seconds() -> u64 {
    0
}

fn take_screenshot() -> Result<(PathBuf, String), String> {
    let monitors = xcap::Monitor::all().map_err(|e| e.to_string())?;
    let monitor = monitors.into_iter().next().ok_or_else(|| "没有可用显示器".to_string())?;
    let image = monitor.capture_image().map_err(|e| e.to_string())?;
    let dir = journal_dir();
    fs::create_dir_all(&dir).map_err(|e| e.to_string())?;
    let path = dir.join(format!("shot-{}.png", now_secs()));
    image.save(&path).map_err(|e| e.to_string())?;
    let bytes = fs::read(&path).map_err(|e| e.to_string())?;
    Ok((path, B64.encode(bytes)))
}

fn handle_input(body: &str, freeze: &AtomicBool) -> Result<serde_json::Value, String> {
    if freeze.load(Ordering::SeqCst) {
        return Err("键鼠注入已急停".into());
    }
    let value: serde_json::Value = serde_json::from_str(body).unwrap_or_else(|_| json!({}));
    let kind = value.get("type").and_then(|v| v.as_str()).unwrap_or("");
    let mut enigo = enigo::Enigo::new(&enigo::Settings::default()).map_err(|e| e.to_string())?;
    use enigo::{Axis, Button, Coordinate, Direction, Key, Keyboard, Mouse};

    match kind {
        "move" => {
            let x = value.get("x").and_then(|v| v.as_i64()).unwrap_or(0) as i32;
            let y = value.get("y").and_then(|v| v.as_i64()).unwrap_or(0) as i32;
            let relative = value.get("relative").and_then(|v| v.as_bool()).unwrap_or(false);
            if relative {
                enigo
                    .move_mouse(x, y, Coordinate::Rel)
                    .map_err(|e| e.to_string())?;
            } else {
                enigo
                    .move_mouse(x, y, Coordinate::Abs)
                    .map_err(|e| e.to_string())?;
            }
        }
        "click" => {
            if let (Some(x), Some(y)) = (
                value.get("x").and_then(|v| v.as_i64()),
                value.get("y").and_then(|v| v.as_i64()),
            ) {
                enigo.move_mouse(x as i32, y as i32, Coordinate::Abs).map_err(|e| e.to_string())?;
            }
            let button = match value.get("button").and_then(|v| v.as_str()).unwrap_or("left") {
                "right" => Button::Right,
                "middle" => Button::Middle,
                _ => Button::Left,
            };
            enigo
                .button(button, Direction::Click)
                .map_err(|e| e.to_string())?;
        }
        "type" => {
            let text = value
                .get("text")
                .and_then(|v| v.as_str())
                .unwrap_or_default();
            enigo.text(text).map_err(|e| e.to_string())?;
        }
        "key" => {
            let key_name = value.get("key").and_then(|v| v.as_str()).unwrap_or("").to_lowercase();
            let key = match key_name.as_str() {
                "enter" => Key::Return,
                "tab" => Key::Tab,
                "escape" | "esc" => Key::Escape,
                "backspace" => Key::Backspace,
                "delete" => Key::Delete,
                "space" => Key::Space,
                "up" => Key::UpArrow,
                "down" => Key::DownArrow,
                "left" => Key::LeftArrow,
                "right" => Key::RightArrow,
                "home" => Key::Home,
                "end" => Key::End,
                _ if key_name.chars().count() == 1 => Key::Unicode(key_name.chars().next().unwrap()),
                _ => return Err(format!("不支持的按键: {key_name}")),
            };
            let modifiers = value.get("modifiers").and_then(|v| v.as_array()).cloned().unwrap_or_default();
            let modifier_keys: Vec<Key> = modifiers.iter().filter_map(|v| match v.as_str().unwrap_or("").to_lowercase().as_str() {
                "ctrl" | "control" => Some(Key::Control), "alt" => Some(Key::Alt), "shift" => Some(Key::Shift), "meta" | "win" | "command" => Some(Key::Meta), _ => None,
            }).collect();
            for modifier in &modifier_keys { enigo.key(*modifier, Direction::Press).map_err(|e| e.to_string())?; }
            enigo.key(key, Direction::Click).map_err(|e| e.to_string())?;
            for modifier in modifier_keys.iter().rev() { enigo.key(*modifier, Direction::Release).map_err(|e| e.to_string())?; }
        }
        "scroll" => {
            let amount = value.get("amount").and_then(|v| v.as_i64()).unwrap_or(0) as i32;
            let axis = if value.get("axis").and_then(|v| v.as_str()) == Some("horizontal") { Axis::Horizontal } else { Axis::Vertical };
            enigo.scroll(amount, axis).map_err(|e| e.to_string())?;
        }
        _ => return Err(format!("未知 input 类型: {kind}")),
    }
    Ok(json!({"ok": true}))
}

#[cfg(windows)]
fn foreground_window() -> serde_json::Value {
    use windows_sys::Win32::UI::WindowsAndMessaging::{GetForegroundWindow, GetWindowTextLengthW, GetWindowTextW};
    unsafe {
        let hwnd = GetForegroundWindow();
        let len = GetWindowTextLengthW(hwnd);
        let mut buf = vec![0u16; (len + 1).max(1) as usize];
        let read = GetWindowTextW(hwnd, buf.as_mut_ptr(), buf.len() as i32);
        json!({"title": String::from_utf16_lossy(&buf[..read.max(0) as usize])})
    }
}

#[cfg(target_os = "macos")]
fn foreground_window() -> serde_json::Value {
    use cocoa::{base::{id, nil}, foundation::NSAutoreleasePool};
    use objc::{class, msg_send, sel, sel_impl};
    use std::ffi::CStr;
    unsafe {
        let pool = NSAutoreleasePool::new(nil);
        let workspace: id = msg_send![class!(NSWorkspace), sharedWorkspace];
        let app: id = msg_send![workspace, frontmostApplication];
        let name: id = msg_send![app, localizedName];
        let ptr: *const std::os::raw::c_char = msg_send![name, UTF8String];
        let title = if ptr.is_null() { String::new() } else { CStr::from_ptr(ptr).to_string_lossy().into_owned() };
        let _: () = msg_send![pool, drain];
        json!({"title":title})
    }
}

#[cfg(not(any(windows, target_os = "macos")))]
fn foreground_window() -> serde_json::Value { json!({"title": ""}) }

fn power_state() -> serde_json::Value {
    let manager = match battery::Manager::new() {
        Ok(value) => value,
        Err(error) => return json!({"supported":false,"error":error.to_string()}),
    };
    let mut batteries = match manager.batteries() {
        Ok(value) => value,
        Err(error) => return json!({"supported":false,"error":error.to_string()}),
    };
    let battery = match batteries.next() {
        Some(Ok(value)) => value,
        Some(Err(error)) => return json!({"supported":false,"error":error.to_string()}),
        None => return json!({"supported":false,"reason":"no-battery"}),
    };
    let state = format!("{:?}", battery.state()).to_lowercase();
    json!({
        "supported": true,
        "percent": (battery.state_of_charge().value * 100.0).round(),
        "state": state,
        "plugged": matches!(battery.state(), battery::State::Charging | battery::State::Full),
        "timeToEmptySeconds": battery.time_to_empty().map(|value| value.value.round() as u64)
    })
}

#[cfg(windows)]
mod uia {
    use serde_json::{json, Value};
    use windows::{
        core::BSTR,
        Win32::{
            System::Com::{CoCreateInstance, CoInitializeEx, CLSCTX_INPROC_SERVER, COINIT_APARTMENTTHREADED},
            UI::{
                Accessibility::{
                    CUIAutomation, IUIAutomation, IUIAutomationElement, IUIAutomationInvokePattern,
                    IUIAutomationValuePattern, TreeScope_Descendants, UIA_ButtonControlTypeId,
                    UIA_CheckBoxControlTypeId, UIA_ComboBoxControlTypeId, UIA_DocumentControlTypeId,
                    UIA_EditControlTypeId, UIA_HyperlinkControlTypeId, UIA_InvokePatternId,
                    UIA_ListControlTypeId, UIA_ListItemControlTypeId, UIA_MenuItemControlTypeId,
                    UIA_PaneControlTypeId, UIA_RadioButtonControlTypeId, UIA_TabControlTypeId,
                    UIA_TabItemControlTypeId, UIA_TextControlTypeId, UIA_ValuePatternId,
                    UIA_WindowControlTypeId,
                },
                WindowsAndMessaging::GetForegroundWindow,
            },
        },
    };

    unsafe fn automation() -> Result<IUIAutomation, String> {
        let _ = CoInitializeEx(None, COINIT_APARTMENTTHREADED);
        CoCreateInstance(&CUIAutomation, None, CLSCTX_INPROC_SERVER).map_err(|e| e.to_string())
    }

    fn type_name(id: i32) -> &'static str {
        match id {
            x if x == UIA_ButtonControlTypeId.0 => "button",
            x if x == UIA_CheckBoxControlTypeId.0 => "checkbox",
            x if x == UIA_ComboBoxControlTypeId.0 => "combobox",
            x if x == UIA_DocumentControlTypeId.0 => "document",
            x if x == UIA_EditControlTypeId.0 => "edit",
            x if x == UIA_HyperlinkControlTypeId.0 => "link",
            x if x == UIA_ListControlTypeId.0 => "list",
            x if x == UIA_ListItemControlTypeId.0 => "listitem",
            x if x == UIA_MenuItemControlTypeId.0 => "menuitem",
            x if x == UIA_PaneControlTypeId.0 => "pane",
            x if x == UIA_RadioButtonControlTypeId.0 => "radio",
            x if x == UIA_TabControlTypeId.0 => "tab",
            x if x == UIA_TabItemControlTypeId.0 => "tabitem",
            x if x == UIA_TextControlTypeId.0 => "text",
            x if x == UIA_WindowControlTypeId.0 => "window",
            _ => "control",
        }
    }

    unsafe fn elements() -> Result<Vec<IUIAutomationElement>, String> {
        let automation = automation()?;
        let root = automation.ElementFromHandle(GetForegroundWindow()).map_err(|e| e.to_string())?;
        let condition = automation.CreateTrueCondition().map_err(|e| e.to_string())?;
        let array = root.FindAll(TreeScope_Descendants, &condition).map_err(|e| e.to_string())?;
        let length = array.Length().map_err(|e| e.to_string())?.min(300);
        let mut out = Vec::with_capacity(length as usize + 1);
        out.push(root);
        for index in 0..length {
            if let Ok(element) = array.GetElement(index) { out.push(element); }
        }
        Ok(out)
    }

    unsafe fn summary(element: &IUIAutomationElement, index: usize) -> Value {
        let name = element.CurrentName().map(|v| v.to_string()).unwrap_or_default();
        let automation_id = element.CurrentAutomationId().map(|v| v.to_string()).unwrap_or_default();
        let control_type = element.CurrentControlType().map(|v| v.0).unwrap_or_default();
        let enabled = element.CurrentIsEnabled().map(|v| v.as_bool()).unwrap_or(false);
        let rect = element.CurrentBoundingRectangle().ok();
        json!({
            "ref": format!("uia-{index}"), "name": name, "automationId": automation_id,
            "controlType": type_name(control_type), "controlTypeId": control_type, "enabled": enabled,
            "bounds": rect.map(|r| json!({"left":r.left,"top":r.top,"right":r.right,"bottom":r.bottom}))
        })
    }

    pub fn tree() -> Result<Value, String> {
        unsafe {
            let list = elements()?;
            let controls: Vec<Value> = list.iter().enumerate().filter_map(|(i, e)| {
                let value = summary(e, i);
                let useful = value["enabled"].as_bool().unwrap_or(false)
                    && (value["name"].as_str().unwrap_or("").len() > 0 || value["automationId"].as_str().unwrap_or("").len() > 0);
                useful.then_some(value)
            }).take(160).collect();
            Ok(json!({"supported":true,"controls":controls}))
        }
    }

    pub fn action(body: &str) -> Result<Value, String> {
        let request: Value = serde_json::from_str(body).map_err(|e| e.to_string())?;
        let target_ref = request.get("ref").and_then(|v| v.as_str()).unwrap_or("");
        let automation_id = request.get("automationId").and_then(|v| v.as_str()).unwrap_or("");
        let name = request.get("name").and_then(|v| v.as_str()).unwrap_or("");
        let kind = request.get("action").and_then(|v| v.as_str()).unwrap_or("invoke");
        unsafe {
            let list = elements()?;
            let target = list.iter().enumerate().find(|(index, element)| {
                if target_ref == format!("uia-{index}") { return true; }
                let id_matches = !automation_id.is_empty() && element.CurrentAutomationId().map(|v| v.to_string() == automation_id).unwrap_or(false);
                let name_matches = !name.is_empty() && element.CurrentName().map(|v| v.to_string() == name).unwrap_or(false);
                id_matches || name_matches
            }).map(|(_, element)| element).ok_or_else(|| "UIA 控件已失效，需要重新读取控件树".to_string())?;
            match kind {
                "invoke" => {
                    let pattern: IUIAutomationInvokePattern = target.GetCurrentPatternAs(UIA_InvokePatternId).map_err(|e| e.to_string())?;
                    pattern.Invoke().map_err(|e| e.to_string())?;
                }
                "setValue" => {
                    let value = request.get("value").and_then(|v| v.as_str()).unwrap_or("");
                    let pattern: IUIAutomationValuePattern = target.GetCurrentPatternAs(UIA_ValuePatternId).map_err(|e| e.to_string())?;
                    pattern.SetValue(&BSTR::from(value)).map_err(|e| e.to_string())?;
                }
                _ => return Err(format!("未知 UIA 动作: {kind}")),
            }
            Ok(json!({"ok":true,"method":"uia","action":kind}))
        }
    }
}

#[cfg(target_os = "macos")]
mod uia {
    use accessibility::{AXAttribute, AXUIElement, AXUIElementActions, AXUIElementAttributes};
    use accessibility_sys::AXIsProcessTrusted;
    use cocoa::{base::{id, nil}, foundation::NSAutoreleasePool};
    use core_foundation::{base::{CFType, TCFType}, string::CFString};
    use objc::{class, msg_send, sel, sel_impl};
    use serde_json::{json, Value};

    fn frontmost_application() -> Result<AXUIElement, String> {
        unsafe {
            if !AXIsProcessTrusted() { return Err("需要在系统设置 → 隐私与安全性 → 辅助功能中授权 Pattern".into()); }
            let pool = NSAutoreleasePool::new(nil);
            let workspace: id = msg_send![class!(NSWorkspace), sharedWorkspace];
            let app: id = msg_send![workspace, frontmostApplication];
            let pid: i32 = msg_send![app, processIdentifier];
            let element = AXUIElement::application(pid);
            let _: () = msg_send![pool, drain];
            Ok(element)
        }
    }

    fn collect(element: &AXUIElement, out: &mut Vec<AXUIElement>, depth: usize) {
        if out.len() >= 300 || depth > 12 { return; }
        out.push(element.clone());
        if let Ok(children) = element.children() {
            for child in children.iter() { collect(&child, out, depth + 1); if out.len() >= 300 { break; } }
        }
    }

    fn elements() -> Result<Vec<AXUIElement>, String> {
        let root = frontmost_application()?;
        let mut out = Vec::new(); collect(&root, &mut out, 0); Ok(out)
    }

    fn text(value: Result<CFString, accessibility::Error>) -> String { value.map(|v| v.to_string()).unwrap_or_default() }

    pub fn tree() -> Result<Value, String> {
        let controls: Vec<Value> = elements()?.iter().enumerate().filter_map(|(index, element)| {
            let name = { let title=text(element.title()); if title.is_empty() { text(element.description()) } else { title } };
            let identifier = text(element.identifier());
            let role = text(element.role()).trim_start_matches("AX").to_lowercase();
            if name.is_empty() && identifier.is_empty() { return None; }
            Some(json!({"ref":format!("ax-{index}"),"name":name,"automationId":identifier,"controlType":role,"enabled":true,"bounds":Value::Null}))
        }).take(160).collect();
        Ok(json!({"supported":true,"platform":"macos-ax","controls":controls}))
    }

    pub fn action(body: &str) -> Result<Value, String> {
        let request: Value = serde_json::from_str(body).map_err(|e| e.to_string())?;
        let target_ref = request.get("ref").and_then(|v| v.as_str()).unwrap_or("");
        let identifier = request.get("automationId").and_then(|v| v.as_str()).unwrap_or("");
        let name = request.get("name").and_then(|v| v.as_str()).unwrap_or("");
        let list = elements()?;
        let target = list.iter().enumerate().find(|(index, element)| {
            if target_ref == format!("ax-{index}") { return true; }
            (!identifier.is_empty() && text(element.identifier()) == identifier)
                || (!name.is_empty() && { let title=text(element.title()); title == name || (title.is_empty() && text(element.description()) == name) })
        }).map(|(_, element)| element).ok_or_else(|| "AX 控件已失效，需要重新读取控件树".to_string())?;
        match request.get("action").and_then(|v| v.as_str()).unwrap_or("invoke") {
            "invoke" => target.press().map_err(|e| e.to_string())?,
            "setValue" => {
                let value = CFString::new(request.get("value").and_then(|v| v.as_str()).unwrap_or(""));
                let generic = unsafe { CFType::wrap_under_get_rule(value.as_CFTypeRef()) };
                target.set_attribute(&AXAttribute::value(), generic).map_err(|e| e.to_string())?;
            }
            kind => return Err(format!("未知 AX 动作: {kind}")),
        }
        Ok(json!({"ok":true,"method":"ax","action":request["action"]}))
    }
}

#[cfg(not(any(windows, target_os = "macos")))]
mod uia {
    use serde_json::{json, Value};
    pub fn tree() -> Result<Value, String> { Ok(json!({"supported":false,"controls":[]})) }
    pub fn action(_: &str) -> Result<Value, String> { Err("当前平台尚未实现无障碍树".into()) }
}

pub fn start_bridge(notify: Arc<Mutex<Option<Box<dyn Fn(String, String) + Send>>>>) -> BridgeHandle {
    let token = format!("br-{}", now_secs());
    let freeze = Arc::new(AtomicBool::new(false));
    let freeze_thread = freeze.clone();
    let notify = notify.clone();
    let server = Server::http("127.0.0.1:0").expect("OS bridge listen failed");
    let port = server.server_addr().to_ip().map(|s| s.port()).unwrap_or(0);
    let url = format!("http://127.0.0.1:{port}");
    let token_for_thread = token.clone();

    thread::spawn(move || {
        for mut request in server.incoming_requests() {
            if !check_auth(&request, &token_for_thread) {
                let _ = request.respond(unauthorized());
                continue;
            }
            let url = request.url().to_string();
            let method = request.method().clone();
            let response = match (method, url.as_str()) {
                (Method::Get, "/idle") => {
                    json_response(json!(IdleResponse {
                        idle_seconds: idle_seconds()
                    }))
                }
                (Method::Get, "/foreground") => json_response(foreground_window()),
                (Method::Get, "/power") => json_response(power_state()),
                (Method::Get, "/accessibility/tree") => match uia::tree() {
                    Ok(value) => json_response(value),
                    Err(error) => Response::from_string(error).with_status_code(StatusCode(500)),
                },
                (Method::Post, "/accessibility/action") => {
                    let body = read_body(&mut request);
                    match uia::action(&body) {
                        Ok(value) => json_response(value),
                        Err(error) => Response::from_string(error).with_status_code(StatusCode(400)),
                    }
                }
                (Method::Post, "/notify") => {
                    let body = read_body(&mut request);
                    let value: serde_json::Value = serde_json::from_str(&body).unwrap_or_else(|_| json!({}));
                    let title = value
                        .get("title")
                        .and_then(|v| v.as_str())
                        .unwrap_or("Pattern")
                        .to_string();
                    let text = value
                        .get("body")
                        .and_then(|v| v.as_str())
                        .unwrap_or("")
                        .to_string();
                    if let Ok(guard) = notify.lock() {
                        if let Some(cb) = guard.as_ref() {
                            cb(title, text);
                        }
                    }
                    json_response(json!({"ok": true}))
                }
                (Method::Post, "/screenshot") => match take_screenshot() {
                    Ok((path, b64)) => json_response(json!({
                        "path": path.to_string_lossy(),
                        "pngBase64": b64
                    })),
                    Err(error) => Response::from_string(error).with_status_code(StatusCode(500)),
                },
                (Method::Post, "/input") => {
                    let body = read_body(&mut request);
                    match handle_input(&body, &freeze_thread) {
                        Ok(value) => json_response(value),
                        Err(error) => Response::from_string(error).with_status_code(StatusCode(400)),
                    }
                }
                (Method::Post, "/freeze") => {
                    let body = read_body(&mut request);
                    let value: serde_json::Value = serde_json::from_str(&body).unwrap_or_else(|_| json!({}));
                    let frozen = value.get("frozen").and_then(|v| v.as_bool()).unwrap_or(true);
                    freeze_thread.store(frozen, Ordering::SeqCst);
                    json_response(json!({"frozen": frozen}))
                }
                _ => Response::from_string("not found").with_status_code(StatusCode(404)),
            };
            let _ = request.respond(response);
        }
    });

    BridgeHandle { url, token, freeze }
}

impl BridgeHandle {
    pub fn set_frozen(&self, frozen: bool) {
        self.freeze.store(frozen, Ordering::SeqCst);
    }

    pub fn is_frozen(&self) -> bool {
        self.freeze.load(Ordering::SeqCst)
    }
}
