"""Create an offline, private pairing PNG. Requires qrcode==8.2 and pypng.

The image contains credentials: keep it in an ignored local directory and never
upload it to an online QR generator, issue, chat, or release.
"""

import argparse
import base64
import json
from pathlib import Path
from urllib.parse import urlsplit
from uuid import UUID

import qrcode
from qrcode.image.pure import PyPNGImage


def pairing_payload(source):
    device = source["DeviceId"]
    phone = source["PhoneDeviceId"]
    UUID(device)
    UUID(phone)
    lan = urlsplit(source["LanUrl"])
    nas = urlsplit(source["NasBaseUrl"])
    for endpoint in (lan, nas):
        if not endpoint.hostname or endpoint.username or endpoint.password or endpoint.query or endpoint.fragment:
            raise ValueError("Pairing requires credential-free endpoint URLs.")
    if lan.scheme not in ("http", "https") or nas.scheme != "https":
        raise ValueError("LAN requires HTTP(S); NAS requires HTTPS.")
    token = source["LanToken"]
    user = source["NasUserName"]
    password = source["NasPassword"]
    if not token or not user or not password or ":" in user:
        raise ValueError("Invalid pairing credentials.")
    basic = base64.b64encode(f"{user}:{password}".encode()).decode("ascii")
    return {
        "version": 1,
        "deviceId": device,
        "phoneDeviceId": phone,
        "lanUrl": source["LanUrl"],
        "lanAuthorization": f"Bearer {token}",
        "nasUrl": source["NasBaseUrl"].rstrip("/") + f"/api/lan-first/v1/devices/{device}/clipboard",
        "nasAuthorization": f"Basic {basic}",
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("private_pairing", type=Path)
    parser.add_argument("output_png", type=Path)
    args = parser.parse_args()
    source = json.loads(args.private_pairing.read_text(encoding="utf-8-sig"))
    payload = json.dumps(pairing_payload(source), ensure_ascii=True, separators=(",", ":"))
    qr = qrcode.QRCode(error_correction=qrcode.constants.ERROR_CORRECT_M, box_size=8, border=4)
    qr.add_data(payload)
    qr.make(fit=True)
    # Exclusive creation prevents accidentally replacing a previously paired identity.
    with args.output_png.open("xb") as target:
        qr.make_image(image_factory=PyPNGImage).save(target)
    print("Created private pairing PNG locally. Do not share this image.")


if __name__ == "__main__":
    main()
