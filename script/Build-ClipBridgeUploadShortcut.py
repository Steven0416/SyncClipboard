"""Build a credential-free iOS upload preview using Actions AppIntents.

The pairing and pending keys must already exist on the phone. This preview uses
x-error for network fallback; iPhone execution remains a required release gate.
"""

import argparse
import json
import plistlib
from pathlib import Path
from urllib.parse import quote
from uuid import uuid4

NAME = "CB-Upload-Auto-Preview"
PAIR = "clipbridge-pairing-v1"
PENDING = "clipbridge-upload-pending-v1"
TEAM = "YG56YK5RN5"
actions = []
OUTPUT_NAMES = {
    "is.workflow.actions.getclipboard": "Clipboard",
    "is.workflow.actions.detect.text": "Text",
    "is.workflow.actions.gettext": "Text",
    "is.workflow.actions.detect.dictionary": "Dictionary",
    "is.workflow.actions.dictionary": "Dictionary",
    "is.workflow.actions.getvalueforkey": "Dictionary Value",
    "com.sindresorhus.Actions.KeychainGetIntent": "Keychain Value",
    "com.sindresorhus.Actions.GenerateUUIDIntent": "UUID",
    "com.sindresorhus.Actions.GetContentsOfURLExtendedIntent": "HTTP Response",
}


def uid():
    return str(uuid4()).upper()


def add(identifier, **parameters):
    parameters.setdefault("UUID", uid())
    actions.append({"WFWorkflowActionIdentifier": identifier, "WFWorkflowActionParameters": parameters})
    return {"Type": "ActionOutput", "OutputUUID": parameters["UUID"],
            "OutputName": OUTPUT_NAMES.get(identifier, "Result")}


def attach(value):
    return {"Value": value, "WFSerializationType": "WFTextTokenAttachment"}


def text(value):
    if isinstance(value, dict):
        return {"Value": {"string": "\ufffc", "attachmentsByRange": {"{0, 1}": value}},
                "WFSerializationType": "WFTextTokenString"}
    return value


def literal(value):
    return {"Value": {"string": value}, "WFSerializationType": "WFTextTokenString"}


def variable(name):
    return {"Type": "Variable", "VariableName": name}


def set_variable(name, value):
    add("is.workflow.actions.setvariable", WFVariableName=name, WFInput=attach(value))
    return variable(name)


def plain(value):
    return add("is.workflow.actions.gettext", WFTextActionText=text(value))


def as_text(value):
    return add("is.workflow.actions.detect.text", WFInput=attach(value))


def dictionary(values):
    fields = [{"WFKey": literal(key), "WFItemType": 0,
               "WFValue": text(value) if isinstance(value, dict) else literal(str(value))}
              for key, value in values.items()]
    return add("is.workflow.actions.dictionary", WFItems={"Value": {"WFDictionaryFieldValueItems": fields},
                                                          "WFSerializationType": "WFDictionaryFieldValue"})


def field(source, key):
    return add("is.workflow.actions.getvalueforkey", WFInput=attach(source), WFDictionaryKey=key)


def app(intent, **parameters):
    parameters["AppIntentDescriptor"] = {"TeamIdentifier": TEAM, "BundleIdentifier": "com.sindresorhus.Actions",
                                         "Name": "Actions", "AppIntentIdentifier": intent}
    return add("com.sindresorhus.Actions." + intent, **parameters)


def key_get(key):
    value = app("KeychainGetIntent", key={"value": key, "title": {"key": key}, "subtitle": {"key": key}})
    return add("is.workflow.actions.detect.dictionary", WFInput=attach(value))


def key_set(key, value):
    # This matches the user's exported Set action with Sync to iCloud disabled.
    app("KeychainSetIntent", key=key, value=text(value))


def begin(value, condition=4, expected=None, numeric=False):
    # Dictionary Value is polymorphic. iOS cannot select a text comparison
    # parameter until its input has an explicit text type, even if the literal
    # comparison value is present in the plist. Keep equality/inequality intact.
    if expected is not None and not numeric:
        value = as_text(value)
    group = uid()
    params = {"GroupingIdentifier": group, "WFControlFlowMode": 0, "WFCondition": condition,
              "WFInput": {"Type": "Variable", "Variable": attach(value)}}
    if expected is not None:
        params["WFNumberValue" if numeric else "WFConditionalActionString"] = (
            str(expected) if numeric else text(expected))
    add("is.workflow.actions.conditional", **params)
    return group


def otherwise(group):
    add("is.workflow.actions.conditional", GroupingIdentifier=group, WFControlFlowMode=1)


def end(group):
    add("is.workflow.actions.conditional", GroupingIdentifier=group, WFControlFlowMode=2)


def stop(message=None, clear=False):
    if clear:
        key_set(PENDING, "{}")
    if message:
        add("is.workflow.actions.showresult", Text=text(message))
    add("is.workflow.actions.output", WFOutput="")


def guard(value, expected, message):
    group = begin(value, 5, expected)
    stop(message, clear=True)
    end(group)


def launch(route, fallback=False):
    base = "shortcuts://x-callback-url/run-shortcut?name=" + quote(NAME, safe="")
    url = base + "&input=text&text=" + route
    if fallback:
        target = "shortcuts://run-shortcut?name=" + quote(NAME, safe="") + "&input=text&text=NAS"
        url += "&x-error=" + quote(target, safe="")
    value = add("is.workflow.actions.url", WFURLActionURL=url)
    add("is.workflow.actions.openurl", WFInput=attach(value))
    stop()


def property_ref(value, name):
    result = dict(value)
    result["Aggrandizements"] = [{"Type": "WFPropertyVariableAggrandizement", "PropertyName": name,
                                  "PropertyUserInfo": {"WFLinkEntityContentPropertyUserInfoPropertyIdentifier": name}}]
    return result


def diagnostic_flag(name, value, uuid=False):
    if uuid:
        pattern = r"^(?:[0-9a-fA-F]{32}|[0-9a-fA-F]{8}(?:-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12})$"
        value = add("is.workflow.actions.text.match", WFMatchTextPattern=pattern, text=text(as_text(value)))
    group = begin(value, 100)
    result = set_variable(name, plain("yes"))
    otherwise(group)
    set_variable(name, plain("no"))
    end(group)
    return result


def failure_report(response, code, body):
    # Report only status and validity flags. Never display request text or headers.
    error = add("is.workflow.actions.detect.dictionary", WFInput=attach(property_ref(response, "bodyText")))
    server_status = field(error, "status")
    server_title = field(error, "title")
    valid_request = diagnostic_flag("DiagnosticRequestIdValid", body["requestId"], uuid=True)
    valid_source = diagnostic_flag("DiagnosticSourceIdValid", body["sourceDeviceId"], uuid=True)
    details = {"operation": body["operation"], "http": code, "serverStatus": server_status,
               "serverTitle": server_title, "requestIdValid": valid_request, "sourceDeviceIdValid": valid_source}
    if "expectedHash" in body:
        details["expectedHashPresent"] = diagnostic_flag("DiagnosticHashPresent", body["expectedHash"])
    report = as_text(dictionary(details))
    add("is.workflow.actions.showresult", Text=text(report))


def request(body):
    encoded = as_text(dictionary(body))
    response = app("GetContentsOfURLExtendedIntent", method="POST", followRedirects=False,
                   timeout=attach(variable("Deadline")), headers=attach(variable("Headers")),
                   body=attach(encoded), url=text(variable("Endpoint")), **{"Show-headersList": False})
    code = property_ref(response, "statusCode")
    failure = begin(code, 5, "200", numeric=True)
    lan = begin(variable("Route"), 4, "LAN")
    for retry in (408, 502, 503, 504):
        transient = begin(code, 4, str(retry), numeric=True)
        launch("NAS")
        end(transient)
    end(lan)
    # Normal return for 401/403/409/422/429 etc. prevents x-error fallback.
    failure_report(response, code, body)
    stop()
    end(failure)
    decoded = add("is.workflow.actions.detect.dictionary", WFInput=attach(property_ref(response, "bodyText")))
    guard(field(decoded, "deviceId"), variable("DeviceId"), "Stopped: response device mismatch")
    guard(field(decoded, "requestId"), body["requestId"], "Stopped: response request mismatch")
    guard(field(decoded, "status"), "ok", "Stopped: clipboard response was not successful")
    profile = field(decoded, "profile")
    guard(field(profile, "type"), "Text", "Stopped: only text is supported")
    data = field(profile, "hasData")
    nontext = begin(data, 5, "0", numeric=True)
    stop("Stopped: response contains file data", clear=True)
    end(nontext)
    result_hash = field(profile, "hash")
    empty_hash = begin(result_hash, 101)
    stop("Stopped: empty profile hash", clear=True)
    end(empty_hash)
    return profile, result_hash


def build():
    add("is.workflow.actions.comment", WFCommentActionText=(
        "ClipBridge upload preview. Requires Actions and existing local pairing. "
        "Do not rename: callbacks use this shortcut name. Reads local Keychain; no embedded credentials. "
        "Pending text and write ID are shared by LAN/NAS. Test with disposable text. "
        "This is not a final 0.2 release; unexpected action failures can invoke x-error fallback."))
    incoming = as_text({"Type": "ExtensionInput"})
    route_nas = begin(incoming, 4, "NAS")
    set_variable("Route", plain("NAS"))
    otherwise(route_nas)
    route_lan = begin(incoming, 4, "LAN")
    set_variable("Route", plain("LAN"))
    otherwise(route_lan)
    # Only the interactive entry captures the phone clipboard. Callback stages never recapture it.
    existing = key_get(PENDING)
    active = begin(field(existing, "state"), 4, "active")
    add("is.workflow.actions.alert", WFAlertActionTitle="Resume pending upload?",
        WFAlertActionMessage="Continue the previously saved text, not the current clipboard.", WFAlertActionCancelButtonShown=True)
    launch("LAN", fallback=True)
    end(active)
    clipboard = add("is.workflow.actions.getclipboard")
    upload = as_text(clipboard)
    write_id = app("GenerateUUIDIntent")
    state = dictionary({"version": "1", "state": "active", "requestId": write_id,
                        "text": upload, "expectedHash": ""})
    key_set(PENDING, as_text(state))
    launch("LAN", fallback=True)
    end(route_lan)
    end(route_nas)

    pairing = key_get(PAIR)
    guard(field(pairing, "version"), "1", "Stopped: unsupported pairing version")
    set_variable("DeviceId", field(pairing, "deviceId"))
    source = field(pairing, "phoneDeviceId")
    pending = key_get(PENDING)
    state = begin(field(pending, "state"), 5, "active")
    stop("No active upload. Run the shortcut directly to start one.")
    end(state)
    request_id = field(pending, "requestId")
    upload_text = field(pending, "text")
    expected = set_variable("ExpectedHash", field(pending, "expectedHash"))

    nas = begin(variable("Route"), 4, "NAS")
    set_variable("Endpoint", field(pairing, "nasUrl"))
    auth_nas = field(pairing, "nasAuthorization")
    set_variable("Headers", dictionary({"Authorization": auth_nas, "Content-Type": "application/json"}))
    set_variable("Deadline", plain("15"))
    otherwise(nas)
    set_variable("Endpoint", field(pairing, "lanUrl"))
    auth_lan = field(pairing, "lanAuthorization")
    set_variable("Headers", dictionary({"Authorization": auth_lan, "Content-Type": "application/json"}))
    set_variable("Deadline", plain("2"))
    end(nas)

    needs_baseline = begin(expected, 101)
    read_id = app("GenerateUUIDIntent")
    _, baseline_hash = request({"requestId": read_id, "sourceDeviceId": source, "operation": "get"})
    set_variable("ExpectedHash", baseline_hash)
    updated = dictionary({"version": "1", "state": "active", "requestId": request_id,
                          "text": upload_text, "expectedHash": variable("ExpectedHash")})
    # Persist the baseline before sending any write, so a lost reply cannot acquire a new baseline.
    key_set(PENDING, as_text(updated))
    end(needs_baseline)
    result, _ = request({"requestId": request_id, "sourceDeviceId": source, "operation": "set",
                         "text": upload_text, "expectedHash": variable("ExpectedHash")})
    guard(field(result, "text"), upload_text, "Stopped: returned text differs from pending upload")
    key_set(PENDING, "{}")
    lan_done = begin(variable("Route"), 4, "LAN")
    add("is.workflow.actions.showresult", Text="LAN uploaded")
    otherwise(lan_done)
    add("is.workflow.actions.showresult", Text="NAS uploaded")
    end(lan_done)
    stop()
    return {"WFWorkflowName": NAME, "WFWorkflowClientVersion": "5037.111",
            "WFWorkflowMinimumClientVersion": 900, "WFWorkflowMinimumClientVersionString": "900",
            "WFWorkflowIcon": {"WFWorkflowIconStartColor": -12365313, "WFWorkflowIconGlyphNumber": 61440},
            "WFWorkflowInputContentItemClasses": ["WFStringContentItem"],
            "WFWorkflowTypes": ["WFWorkflowTypeShowInSearch"], "WFWorkflowHasShortcutInputVariables": True,
            "WFWorkflowImportQuestions": [], "WFWorkflowActions": actions}


def validate(workflow):
    seen, groups, producers = set(), [], {}
    def references(value):
        if isinstance(value, dict):
            if "OutputUUID" in value and value["OutputUUID"] not in seen:
                raise ValueError("Forward or missing action reference")
            for item in value.values():
                references(item)
        elif isinstance(value, list):
            for item in value:
                references(item)
    for action in workflow["WFWorkflowActions"]:
        params = action["WFWorkflowActionParameters"]
        references(params)
        if action["WFWorkflowActionIdentifier"] == "is.workflow.actions.conditional":
            mode, group = params["WFControlFlowMode"], params["GroupingIdentifier"]
            if mode == 0:
                if "WFConditionalActionString" in params:
                    source = params["WFInput"]["Variable"]["Value"]
                    if producers.get(source.get("OutputUUID")) != "is.workflow.actions.detect.text":
                        raise ValueError("Text comparisons require an explicitly typed text input")
                groups.append(group)
            elif not groups or groups[-1] != group:
                raise ValueError("Unbalanced conditional")
            elif mode == 2:
                groups.pop()
        if params["UUID"] in seen:
            raise ValueError("Duplicate action UUID")
        seen.add(params["UUID"])
        producers[params["UUID"]] = action["WFWorkflowActionIdentifier"]
    if groups:
        raise ValueError("Unclosed conditional")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    workflow = build()
    validate(workflow)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_bytes(plistlib.dumps(workflow, fmt=plistlib.FMT_XML, sort_keys=False))
    # Round-trip the actual artifact, not just the Python builder objects.
    validate(plistlib.loads(args.output.read_bytes()))
    print(json.dumps({"name": NAME, "actions": len(actions), "embeddedCredentials": False}))


if __name__ == "__main__":
    main()
