"""Execute the emitted plist with mocked iOS actions and a protocol server.

This checks variable wiring/control flow, not Apple's UI or AppIntent runtime.
The iPhone import/execution check remains necessary.
"""

import hashlib
import importlib.util
import json
import plistlib
import re
import unittest
from urllib.parse import parse_qs, urlsplit
from uuid import uuid4
from pathlib import Path

spec = importlib.util.spec_from_file_location("builder", Path(__file__).with_name("Build-ClipBridgeShortcut.py"))
b = importlib.util.module_from_spec(spec)
spec.loader.exec_module(b)


class Halt(Exception):
    pass


class NetworkError(Exception):
    pass


def digest(value):
    return hashlib.sha256(value.encode("utf-8")).hexdigest().upper()


class Phone:
    def __init__(self):
        self.workflow = plistlib.loads(plistlib.dumps(b.build()))["WFWorkflowActions"]
        self.pairing = {"version": 1, "deviceId": uuid4().hex, "phoneDeviceId": uuid4().hex,
                        "lanUrl": "http://syncclip-test.local:5034/api/lan-first/v1/clipboard",
                        "lanAuthorization": "Bearer " + "a" * 32,
                        "nasUrl": "https://relay.example/api/lan-first/v1/devices/" + uuid4().hex + "/clipboard",
                        "nasAuthorization": "Basic dGVzdDp0ZXN0"}
        self.keys = {b.w.PAIR: json.dumps(self.pairing)}
        self.clipboard = "phone text"
        self.desktop = "desktop text"
        self.choice = 0
        self.messages, self.calls, self.urls = [], [], []
        self.completed = {}
        self.fail_lan = False
        self.fail_nas = False
        self.lose_write_reply = False
        self.invalid_json = False
        self.wrong_request = False
        self.wrong_device = False
        self.corrupt_text = False
        self.http_lan = None
        self.desktop_change_before_set = False
        self.depth = 0

    def http(self, url, headers, body, timeout):
        channel = "LAN" if url.startswith("http:") else "NAS"
        request = json.loads(body)
        assert headers["Content-Type"] == "application/json"
        assert headers["Authorization"] == self.pairing["lanAuthorization" if channel == "LAN" else "nasAuthorization"]
        assert timeout == ("4" if channel == "LAN" else "15")
        self.calls.append((channel, request.copy()))
        if (channel == "LAN" and self.fail_lan) or (channel == "NAS" and self.fail_nas):
            raise NetworkError("timeout")
        if channel == "LAN" and self.http_lan:
            return {"statusCode": self.http_lan, "bodyText": "{}"}
        if self.invalid_json:
            return {"statusCode": 200, "bodyText": "invalid json"}
        status = "ok"
        request_id = request["requestId"].lower()
        if request_id in self.completed:
            return self.completed[request_id]
        if request["operation"] == "set":
            if self.desktop_change_before_set:
                self.desktop = "new local copy"
            if request["expectedHash"].upper() != digest(self.desktop):
                status = "conflict"
            else:
                self.desktop = request["text"]
        data = {"requestId": str(uuid4()) if self.wrong_request else request_id,
                "deviceId": uuid4().hex if self.wrong_device else self.pairing["deviceId"],
                "status": status, "isCache": False,
                "profile": {"type": "Text", "hash": digest(self.desktop), "hasData": False,
                            "text": "corrupt" if self.corrupt_text else self.desktop}}
        response = {"statusCode": 200 if status == "ok" else 409, "bodyText": json.dumps(data)}
        if request["operation"] == "set" and status == "ok":
            self.completed[request_id] = response
            if self.lose_write_reply and channel == "LAN":
                # OS changes after the write, before the NAS deduplication retry.
                self.desktop = "newer copy after applied write"
                self.clipboard = "phone changed during fallback"
                raise NetworkError("lost acknowledgement")
        return response

    def run(self, incoming=""):
        self.depth += 1
        if self.depth > 16:
            raise AssertionError("callback loop")
        outputs, variables = {}, {}

        def resolve(value):
            if isinstance(value, list):
                return [resolve(item) for item in value]
            if not isinstance(value, dict):
                return value
            if value.get("WFSerializationType") == "WFDictionaryFieldValue":
                return {resolve(item["WFKey"]): resolve(item["WFValue"])
                        for item in value["Value"]["WFDictionaryFieldValueItems"]}
            if value.get("WFSerializationType") == "WFTextTokenString":
                data = value["Value"]
                result = data["string"]
                # Substitute from the end so variable lengths cannot shift offsets.
                for span, item in sorted(data.get("attachmentsByRange", {}).items(),
                                         key=lambda pair: int(re.findall(r"\d+", pair[0])[0]), reverse=True):
                    pos = int(re.findall(r"\d+", span)[0])
                    encoded = result.encode("utf-16-le")
                    result = (encoded[:pos * 2] + stringify(resolve(item)).encode("utf-16-le") + encoded[(pos + 1) * 2:]).decode("utf-16-le")
                return result
            if value.get("WFSerializationType") == "WFTextTokenAttachment":
                return resolve(value["Value"])
            if value.get("Type") == "ExtensionInput":
                return incoming
            if value.get("Type") == "Variable":
                return resolve(value["Variable"]) if "Variable" in value else variables[value["VariableName"]]
            if value.get("Type") == "ActionOutput":
                result = outputs[value["OutputUUID"]]
                for prop in value.get("Aggrandizements", []):
                    result = result[prop["PropertyName"]]
                return result
            return {key: resolve(item) for key, item in value.items()}

        def stringify(value):
            if isinstance(value, (dict, list, bool)):
                return json.dumps(value, ensure_ascii=False)
            return "" if value is None else str(value)

        # Native control-flow selection: skipped branches produce no output.
        conditions, menu_active = [], None
        active = lambda: all(frame["active"] for frame in conditions) and menu_active is not False
        try:
            for action in self.workflow:
                identifier = action["WFWorkflowActionIdentifier"]
                raw = action["WFWorkflowActionParameters"]
                if identifier.endswith(".conditional"):
                    mode = raw["WFControlFlowMode"]
                    if mode == 0:
                        parent = active()
                        test = False
                        if parent:
                            val = resolve(raw["WFInput"])
                            condition = raw["WFCondition"]
                            expected = resolve(raw.get("WFNumberValue", raw.get("WFConditionalActionString")))
                            if condition in (0, 2):
                                test = float(val) < float(expected) if condition == 0 else float(val) > float(expected)
                            else:
                                test = stringify(val) == stringify(expected)
                                if condition == 5:
                                    test = not test
                        conditions.append({"active": parent and test, "parent": parent, "test": test})
                    elif mode == 1:
                        conditions[-1]["active"] = conditions[-1]["parent"] and not conditions[-1]["test"]
                    else:
                        conditions.pop()
                    continue
                if identifier.endswith(".choosefrommenu"):
                    mode = raw["WFControlFlowMode"]
                    if mode == 0:
                        menu_active = False
                        choices = raw["WFMenuItems"]
                    elif mode == 1:
                        menu_active = raw["WFMenuItemTitle"] == choices[self.choice]
                    else:
                        menu_active = None
                    continue
                if not active():
                    continue
                p = resolve(raw)
                result = None
                kind = identifier.removeprefix("is.workflow.actions.")
                if kind == "comment":
                    pass
                elif kind in ("detect.text", "gettext"):
                    result = stringify(p.get("WFInput", p.get("WFTextActionText")))
                elif kind == "dictionary":
                    result = p["WFItems"]
                elif kind == "detect.dictionary":
                    value = p["WFInput"]
                    result = json.loads(value) if isinstance(value, str) else value
                elif kind == "getvalueforkey":
                    result = p["WFInput"].get(p["WFDictionaryKey"], "")
                elif kind == "text.match":
                    result = re.findall(p["WFMatchTextPattern"], p["text"], re.IGNORECASE)
                elif kind == "count":
                    result = len(p["Input"])
                elif kind == "hash":
                    result = digest(p["WFInput"])
                elif kind == "setvariable":
                    variables[p["WFVariableName"]] = p["WFInput"]
                elif kind == "getclipboard":
                    result = self.clipboard
                elif kind == "setclipboard":
                    self.clipboard = p["WFInput"]
                elif kind == "showresult":
                    self.messages.append(p["Text"])
                elif kind == "output":
                    raise Halt()
                elif kind == "url":
                    result = p["WFURLActionURL"]
                elif kind == "openurl":
                    url = p["WFInput"]
                    self.urls.append(url)
                    query = parse_qs(urlsplit(url).query)
                    assert query["name"] == [b.NAME]
                    try:
                        self.run(query["text"][0])
                    except NetworkError:
                        if "x-error" not in query:
                            raise
                        self.run(parse_qs(urlsplit(query["x-error"][0]).query)["text"][0])
                elif identifier.endswith("KeychainGetIntent"):
                    result = self.keys.get(p["key"]["value"], "")
                elif identifier.endswith("KeychainSetIntent"):
                    self.keys[p["key"]] = p["value"]
                elif identifier.endswith("GenerateUUIDIntent"):
                    result = str(uuid4()).upper()
                elif identifier.endswith("GetContentsOfURLExtendedIntent"):
                    result = self.http(p["url"], p["headers"], p["body"], p["timeout"])
                elif kind == "scanbarcode":
                    result = json.dumps(self.pairing)
                else:
                    raise AssertionError("Unsupported mock action " + identifier)
                outputs[raw["UUID"]] = result
        except Halt:
            pass
        finally:
            self.depth -= 1


class ShortcutTests(unittest.TestCase):
    def test_lan_upload_uses_initial_clipboard(self):
        phone = Phone()
        phone.clipboard = '中文 😀\nquotes " and \\ backslash\n' + 'x' * 12000
        expected = phone.clipboard
        phone.run()
        self.assertEqual(expected, phone.desktop)
        self.assertEqual(["LAN", "LAN"], [channel for channel, _ in phone.calls])
        self.assertEqual("done", json.loads(phone.keys[b.SESSION])["state"])
        self.assertNotIn("wire", json.loads(phone.keys[b.SESSION]))
        self.assertNotIn("text", json.loads(phone.keys[b.SESSION]))

    def test_lan_read(self):
        phone = Phone()
        phone.choice = 1
        phone.desktop = "电脑文字 😀\nnext"
        phone.run()
        self.assertEqual(phone.desktop, phone.clipboard)
        self.assertEqual(1, len(phone.calls))

    def test_nas_fallback_and_next_task_returns_to_lan(self):
        phone = Phone()
        phone.fail_lan = True
        phone.run()
        self.assertEqual(["LAN", "NAS", "NAS"], [x[0] for x in phone.calls])
        phone.fail_lan = False
        phone.calls.clear()
        phone.run()
        self.assertEqual(["LAN", "LAN"], [x[0] for x in phone.calls])

    def test_lost_write_reply_retries_identical_write_without_overwrite(self):
        phone = Phone()
        phone.lose_write_reply = True
        phone.run()
        writes = [request for _, request in phone.calls if request["operation"] == "set"]
        self.assertEqual(2, len(writes))
        self.assertEqual(writes[0], writes[1])
        self.assertEqual("phone text", writes[1]["text"])
        self.assertEqual("newer copy after applied write", phone.desktop)

    def test_conflict_does_not_retry(self):
        phone = Phone()
        phone.desktop_change_before_set = True
        phone.run()
        self.assertEqual("new local copy", phone.desktop)
        self.assertEqual(2, len(phone.calls))
        self.assertIn("停止覆盖", phone.messages[-1])

    def test_http_errors_do_not_fallback_except_transient(self):
        for code in (400, 401, 403, 409, 422, 429, 500):
            with self.subTest(code=code):
                phone = Phone()
                phone.http_lan = code
                phone.run()
                self.assertEqual(1, len(phone.calls))
        for code in (408, 502, 503, 504):
            with self.subTest(code=code):
                phone = Phone()
                phone.http_lan = code
                phone.run()
                self.assertEqual(["LAN", "NAS", "NAS"], [x[0] for x in phone.calls])

    def test_invalid_json_does_not_trigger_nas(self):
        phone = Phone()
        phone.invalid_json = True
        with self.assertRaises(json.JSONDecodeError):
            phone.run()
        self.assertEqual(1, len(phone.calls))

    def test_invalid_response_is_not_copied_or_retried(self):
        for flag in ("wrong_request", "wrong_device", "corrupt_text"):
            with self.subTest(flag=flag):
                phone = Phone()
                phone.choice = 1
                setattr(phone, flag, True)
                phone.run()
                self.assertEqual("phone text", phone.clipboard)
                self.assertEqual(1, len(phone.calls))

    def test_resume_keeps_snapshot_and_wire(self):
        phone = Phone()
        phone.fail_lan = phone.fail_nas = True
        phone.run()
        first_wire = json.loads(phone.keys[b.SESSION])["wire"]
        phone.clipboard = "new phone clipboard"
        phone.fail_lan = phone.fail_nas = False
        phone.choice = 2
        phone.run()
        self.assertEqual("phone text", phone.desktop)
        self.assertEqual(json.loads(first_wire), phone.calls[2][1])

    def test_stale_callback_cannot_send(self):
        phone = Phone()
        phone.fail_lan = phone.fail_nas = True
        phone.run()
        phone.calls.clear()
        phone.run("NAS:" + str(uuid4()))
        self.assertEqual([], phone.calls)

    def test_pairing_and_clear_and_no_secret_in_callbacks(self):
        phone = Phone()
        phone.choice = 3
        phone.run()
        self.assertEqual(phone.pairing, json.loads(phone.keys[b.w.PAIR]))
        phone.choice = 0
        phone.run()
        for url in phone.urls:
            self.assertNotIn(phone.pairing["lanAuthorization"], url)
            self.assertNotIn(phone.pairing["nasAuthorization"], url)
            self.assertNotIn(phone.clipboard, url)
        phone.choice = 4
        phone.run()
        self.assertEqual("idle", json.loads(phone.keys[b.SESSION])["state"])


if __name__ == "__main__":
    unittest.main()
