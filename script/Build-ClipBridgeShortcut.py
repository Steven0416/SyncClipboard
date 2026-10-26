"""Build the credential-free, single-entry iOS 27 text clipboard shortcut.

Actions AppIntents follow the user's iOS exports. All network requests are
isolated from response parsing so x-error cannot turn a parsing failure into a
second write. Only a ticket (never text or credentials) travels in callback URLs.
"""

import argparse
import importlib.util
import json
import plistlib
from pathlib import Path
from urllib.parse import quote

spec = importlib.util.spec_from_file_location("wire", Path(__file__).with_name("Build-ClipBridgeUploadShortcut.py"))
w = importlib.util.module_from_spec(spec)
spec.loader.exec_module(w)
w.OUTPUT_NAMES.update({"is.workflow.actions.hash": "Hash", "is.workflow.actions.scanbarcode": "QR/Barcode",
                       "is.workflow.actions.text.match": "Matches"})
NAME = "ClipBridge"
SESSION = "clipbridge-session-v2"
UUID_PATTERN = r"(?:[0-9a-f]{32}|[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12})"
HASH_PATTERN = r"[0-9a-f]{64}"
STAGES = ("LAN", "NAS", "PROCESS", "FALLBACK", "FAILED")
STATE_FIELDS = ("ticket", "intent", "phase", "wire", "text", "expectedHash", "channel", "state")


def concat(*parts):
    string, attachments = "", {}
    for part in parts:
        if isinstance(part, dict):
            attachments[f"{{{len(string.encode('utf-16-le')) // 2}, 1}}"] = part
            string += "\ufffc"
        else:
            string += part
    return {"Value": {"string": string, "attachmentsByRange": attachments},
            "WFSerializationType": "WFTextTokenString"}


def text_action(value):
    return w.add("is.workflow.actions.gettext", WFTextActionText=value)


def show(message):
    w.add("is.workflow.actions.showresult", Text=message)


def stop(message=None):
    if message:
        show(message)
    # Omit the optional output instead of a dangling/empty magic variable.
    w.add("is.workflow.actions.output")


def matches(value, pattern):
    result = w.add("is.workflow.actions.text.match", text=w.text(w.as_text(value)),
                   WFMatchTextPattern=pattern, WFMatchTextCaseSensitive=False)
    return w.add("is.workflow.actions.count", Input=w.attach(result), WFInput=w.attach(result), WFCountType="Items")


def require_match(value, pattern, message):
    group = w.begin(matches(value, pattern), 0, "1", numeric=True)
    stop(message)
    w.end(group)


def require_literal(value, expected, message):
    group = w.begin(value, 5, expected)
    stop(message)
    w.end(group)


def uuid_equal(actual, expected, message):
    require_match(expected, "^" + UUID_PATTERN + "$", message)
    require_match(actual, concat("^", expected, "$"), message)


def hash_of(value):
    return w.add("is.workflow.actions.hash", WFHashType="SHA256", WFInput=w.attach(w.as_text(value)))


def hash_equal(actual, expected, message):
    require_match(expected, "^" + HASH_PATTERN + "$", message)
    require_match(actual, concat("^", expected, "$"), message)


def pair():
    result = w.key_get(w.PAIR)
    validate_pair(result)
    return result


def validate_pair(value):
    require_literal(w.field(value, "version"), "1", "配对版本不支持，请选择扫码配对。")
    for field in ("deviceId", "phoneDeviceId"):
        require_match(w.field(value, field), "^" + UUID_PATTERN + "$", "配对设备编号无效，请重新配对。")
    require_match(w.field(value, "lanUrl"),
                  r"^http://[a-z0-9-]+\.local:[0-9]{1,5}/api/lan-first/v1/clipboard$",
                  "局域网地址无效；请用 ClipBridge 配对二维码。")
    require_match(w.field(value, "nasUrl"),
                  "^https://[a-z0-9.-]+(?::[0-9]{1,5})?/api/lan-first/v1/devices/" + UUID_PATTERN + "/clipboard$",
                  "NAS 地址必须是配对二维码中的 HTTPS 地址。")
    require_match(w.field(value, "lanAuthorization"), r"^Bearer [a-z0-9._~+/-]{20,}={0,2}$",
                  "局域网配对凭据无效，请重新配对。")
    require_match(w.field(value, "nasAuthorization"), r"^Basic [a-z0-9+/]+={0,2}$",
                  "NAS 配对凭据无效，请重新配对。")


def load_state():
    state = w.key_get(SESSION)
    require_literal(w.field(state, "state"), "active", "没有待继续的任务，请从主菜单开始发送或获取。")
    require_match(w.field(state, "ticket"), "^" + UUID_PATTERN + "$", "任务编号无效，请开始新任务。")
    return state


def complete(channel):
    # Successful tasks must not retain an unnecessary second clipboard history.
    result = w.dictionary({"state": "done", "lastTransport": channel})
    w.key_set(SESSION, w.as_text(result))


def save_state(previous, **changes):
    values = {key: w.field(previous, key) for key in STATE_FIELDS if key not in changes}
    values.update(changes)
    result = w.dictionary(values)
    w.key_set(SESSION, w.as_text(result))
    return result


def callback(stage, ticket, error=None):
    prefix = ("shortcuts://x-callback-url/run-shortcut?name=" if error else "shortcuts://run-shortcut?name=")
    prefix += quote(NAME, safe="")
    prefix += "&input=text&text=" + stage + "%3A"
    parts = [prefix, ticket]
    if error:
        target = "shortcuts://run-shortcut?name=" + quote(NAME, safe="") + "&input=text&text=" + error + ":"
        parts += ["&x-error=" + quote(target, safe=""), ticket]
    url = w.add("is.workflow.actions.url", WFURLActionURL=concat(*parts))
    w.add("is.workflow.actions.openurl", WFInput=w.attach(url))
    stop()


def dispatch(channel, ticket):
    group = w.begin(channel, 4, "NAS")
    callback("NAS", ticket, "FAILED")
    w.otherwise(group)
    callback("LAN", ticket, "FALLBACK")
    w.end(group)


def current_ticket(ticket):
    # Do not let a late reply from a replaced operation overwrite newer state.
    latest = load_state()
    uuid_equal(w.field(latest, "ticket"), ticket, "旧任务回调已忽略。")
    return latest


def network(stage, state):
    pairing = pair()
    ticket = w.field(state, "ticket")
    field_prefix = "lan" if stage == "LAN" else "nas"
    headers = w.dictionary({"Authorization": w.field(pairing, field_prefix + "Authorization"),
                            "Content-Type": "application/json"})
    response = w.app("GetContentsOfURLExtendedIntent", method="POST", followRedirects=False,
                     timeout="4" if stage == "LAN" else "15", headers=w.attach(headers),
                     body=w.attach(w.as_text(w.field(state, "wire"))),
                     url=w.text(w.field(pairing, field_prefix + "Url")))
    current_ticket(ticket)
    save_state(state, channel=stage, code=w.property_ref(response, "statusCode"),
               responseBody=w.property_ref(response, "bodyText"))
    callback("PROCESS", ticket)


def process(state):
    ticket = w.field(state, "ticket")
    channel = w.field(state, "channel")
    code = w.field(state, "code")
    # HTTP responses are deliberately outside the network x-error worker.
    retry = w.begin(matches(code, r"^(408|502|503|504)$"), 2, "0", numeric=True)
    lan = w.begin(channel, 4, "LAN")
    callback("NAS", ticket, "FAILED")
    w.end(lan)
    w.end(retry)
    conflict = w.begin(code, 4, "409")
    stop("电脑剪贴板已变化，已停止覆盖。确认内容后，从菜单开始一次新发送。")
    w.end(conflict)
    require_literal(code, "200", concat("传输停止，HTTP ", code,
                                         "。未自动重发。可检查连接后选择继续上次任务；认证错误请重新配对。"))
    result = w.add("is.workflow.actions.detect.dictionary", WFInput=w.attach(w.field(state, "responseBody")))
    request = w.add("is.workflow.actions.detect.dictionary", WFInput=w.attach(w.field(state, "wire")))
    pairing = pair()
    uuid_equal(w.field(result, "requestId"), w.field(request, "requestId"), "响应请求编号不匹配，操作停止。")
    uuid_equal(w.field(result, "deviceId"), w.field(pairing, "deviceId"), "响应来自其他设备，操作停止。")
    require_literal(w.field(result, "status"), "ok", "服务器未确认成功，操作停止。")
    profile = w.field(result, "profile")
    require_literal(w.field(profile, "type"), "Text", "当前只支持文字剪贴板。")
    require_match(w.field(profile, "hasData"), r"^(false|0|no)$", "响应含独立文件，不能当作完整文字使用。")
    content = w.set_variable("ReceivedText", w.as_text(w.field(profile, "text")))
    remote_hash = w.field(profile, "hash")
    hash_equal(hash_of(content), remote_hash, "返回的文字校验失败，操作停止。")

    baseline = w.begin(w.field(state, "phase"), 4, "baseline")
    write = w.dictionary({"requestId": w.app("GenerateUUIDIntent"),
                          "sourceDeviceId": w.field(pairing, "phoneDeviceId"), "operation": "set",
                          "text": w.field(state, "text"), "expectedHash": remote_hash})
    # Persist the complete write before any channel sends it. A timeout never
    # fetches a new baseline or clipboard snapshot for this operation.
    save_state(state, phase="write", wire=w.as_text(write), expectedHash=remote_hash)
    dispatch(channel, ticket)
    w.end(baseline)

    read = w.begin(w.field(state, "intent"), 4, "read")
    w.add("is.workflow.actions.setclipboard", WFLocalOnly=True, WFInput=w.attach(content))
    complete(channel)
    show(concat("已通过 ", channel, " 获取文字，现可粘贴。"))
    w.otherwise(read)
    require_literal(w.field(state, "phase"), "write", "上传阶段无效，操作停止。")
    hash_equal(remote_hash, hash_of(w.field(state, "text")), "回执与本次发送文字不一致，操作停止。")
    complete(channel)
    show(concat("已通过 ", channel, " 发送文字到电脑。"))
    w.end(read)
    stop()


def new_operation(intent):
    # Snapshot clipboard BEFORE Generate UUID or any other output-producing action.
    content = w.set_variable("UploadText", w.as_text(w.add("is.workflow.actions.getclipboard"))) if intent == "send" else ""
    if intent == "send":
        empty = w.begin(w.character_count(content), 0, "1", numeric=True)
        stop("剪贴板没有可发送的文字。")
        w.end(empty)
        large = w.begin(w.character_count(content), 2, "262144", numeric=True)
        stop("文字过长，本版最多接收 262144 个 UTF-16 单元。")
        w.end(large)
    pairing = pair()
    ticket = w.app("GenerateUUIDIntent")
    wire = w.dictionary({"requestId": w.app("GenerateUUIDIntent"),
                         "sourceDeviceId": w.field(pairing, "phoneDeviceId"), "operation": "get"})
    state = w.dictionary({"ticket": ticket, "intent": intent, "phase": "baseline" if intent == "send" else "read",
                          "wire": w.as_text(wire), "text": content, "expectedHash": "", "channel": "LAN", "state": "active"})
    w.key_set(SESSION, w.as_text(state))
    callback("LAN", ticket, "FALLBACK")


def build():
    w.actions.clear()
    w.add("is.workflow.actions.comment", WFCommentActionText=(
        "ClipBridge 0.2 preview 7 · iOS 27 + Actions\n"
        "一个入口：文字发送、获取、局域网优先、NAS 回退、扫码配对。\n"
        "保留名称 ClipBridge。仅主动运行时同步；不支持图片/文件或后台自动监控。\n"
        "凭据只从本机 Actions Keychain 读取，不在文件和回调 URL 中。\n"
        "一次只运行一个任务。新发送/获取替换旧任务；继续上次任务保留旧文字。\n"
        "LAN 4 秒，NAS 15 秒；认证、冲突、格式及校验错误停止。"))
    incoming = w.set_variable("RouteInput", w.as_text({"Type": "ExtensionInput"}))
    for stage in STAGES:
        group = w.begin(matches(incoming, "^" + stage + ":" + UUID_PATTERN + "$"), 2, "0", numeric=True)
        state = load_state()
        ticket = w.field(state, "ticket")
        require_match(incoming, concat("^", stage, ":", ticket, "$"), "旧任务回调已忽略。")
        if stage in ("LAN", "NAS"):
            network(stage, state)
        elif stage == "PROCESS":
            process(state)
        elif stage == "FALLBACK":
            callback("NAS", ticket, "FAILED")
        else:
            stop("局域网和 NAS 均无法连接。任务保留，可稍后选择继续上次任务。")
        w.end(group)

    items = ["发送当前剪贴板（新任务）", "获取电脑剪贴板（新任务）", "继续上次任务", "扫码配对", "清除待处理任务", "使用说明"]
    menu = w.uid()
    w.add("is.workflow.actions.choosefrommenu", GroupingIdentifier=menu, WFControlFlowMode=0,
          WFMenuPrompt="ClipBridge · 局域网优先 / NAS 回退", WFMenuItems=items)
    for index, title in enumerate(items):
        w.add("is.workflow.actions.choosefrommenu", GroupingIdentifier=menu, WFControlFlowMode=1, WFMenuItemTitle=title)
        if index < 2:
            new_operation("send" if index == 0 else "read")
        elif index == 2:
            state = load_state()
            callback("LAN", w.field(state, "ticket"), "FALLBACK")
        elif index == 3:
            scan = w.add("is.workflow.actions.scanbarcode")
            pairing = w.add("is.workflow.actions.detect.dictionary", WFInput=w.attach(scan))
            validate_pair(pairing)
            w.key_set(w.PAIR, w.as_text(pairing))
            w.key_set(SESSION, '{"state":"idle"}')
            stop("配对已保存在本机。现在可从本快捷指令发送或获取文字。")
        elif index == 4:
            w.key_set(SESSION, '{"state":"idle"}')
            stop("待处理任务已清除，配对保留。")
        else:
            stop("先复制文字，再运行 ClipBridge → 发送；获取后直接粘贴。\n"
                 "每次新任务先试局域网，连不上则使用 HTTPS NAS。电脑需运行 ClipBridge 预览客户端。\n"
                 "现有 clipbridge-pairing-v1 配对可直接使用；新手机选择扫码配对。\n"
                 "请保留快捷指令名称 ClipBridge，允许首次网络/Actions/剪贴板访问。\n"
                 "继续上次任务会发送原来保存的文字。新任务会替换待处理任务，请勿并行运行。\n"
                 "仅支持文字，需主动运行。本文件不包含账号密码。")
    w.add("is.workflow.actions.choosefrommenu", GroupingIdentifier=menu, WFControlFlowMode=2)
    stop()
    return {"WFWorkflowName": NAME, "WFWorkflowClientVersion": "5037.111",
            "WFWorkflowMinimumClientVersion": 900, "WFWorkflowMinimumClientVersionString": "900",
            "WFWorkflowIcon": {"WFWorkflowIconStartColor": -12365313, "WFWorkflowIconGlyphNumber": 61440},
            "WFWorkflowInputContentItemClasses": ["WFStringContentItem"],
            "WFWorkflowTypes": ["WFWorkflowTypeShowInSearch"], "WFWorkflowHasShortcutInputVariables": True,
            "WFWorkflowImportQuestions": [], "WFWorkflowActions": w.actions}


def validate(workflow):
    w.validate(workflow)
    stack = []
    for action in workflow["WFWorkflowActions"]:
        params = action["WFWorkflowActionParameters"]
        if action["WFWorkflowActionIdentifier"] not in ("is.workflow.actions.conditional", "is.workflow.actions.choosefrommenu"):
            continue
        mode, group = params["WFControlFlowMode"], params["GroupingIdentifier"]
        if mode == 0:
            stack.append(group)
        elif not stack or stack[-1] != group:
            raise ValueError("Unbalanced menu/conditional")
        elif mode == 2:
            stack.pop()
    if stack:
        raise ValueError("Unclosed menu/conditional")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    workflow = build()
    validate(workflow)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_bytes(plistlib.dumps(workflow, fmt=plistlib.FMT_XML, sort_keys=False))
    validate(plistlib.loads(args.output.read_bytes()))
    print(json.dumps({"name": NAME, "actions": len(w.actions), "embeddedCredentials": False}))


if __name__ == "__main__":
    main()
