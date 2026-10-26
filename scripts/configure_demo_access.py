#!/usr/bin/env python3
"""Create private deployment settings and App Review credentials without printing secrets."""
import argparse
import base64
import hashlib
import json
import os
from pathlib import Path
import secrets
import shlex
import uuid


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output-dir', type=Path,
                        default=Path(__file__).resolve().parents[1] / '.private')
    args = parser.parse_args()
    args.output_dir.mkdir(mode=0o700, parents=True, exist_ok=True)
    paths = [args.output_dir / name for name in
             ('demo-access.env', 'demo-access.azure.json', 'app-review-credentials.txt')]
    if any(path.exists() for path in paths):
        parser.error('Demo settings already exist. Choose a new output directory to generate a new dataset.')

    password = secrets.token_urlsafe(24)
    salt = secrets.token_bytes(32)
    settings = {
        'DemoAccess__Enabled': 'true',
        'DemoAccess__Username': 'appreview',
        'DemoAccess__AccountId': str(uuid.uuid4()),
        'DemoAccess__PasswordSalt': base64.b64encode(salt).decode(),
        'DemoAccess__PasswordHash': base64.b64encode(
            hashlib.pbkdf2_hmac('sha256', password.encode(), salt, 600_000, 32)).decode(),
    }
    contents = [
        ''.join(f'export {key}={shlex.quote(value)}\n' for key, value in settings.items()),
        json.dumps(settings, indent=2) + '\n',
        'TrustMap App Review\n\nUsername: appreview\nPassword: ' + password + '\n\n'
        'Open Demo access in the row beside Privacy Policy and Support.\n'
        'Sample places are in London. Open Places to browse them without location access.\n'
        'Sunday Table · Demo has reviews by Alex and Eli. Touch and hold Eli’s review to report it.\n'
        'Tap Eli’s name, then the profile menu (…) to block.\n'
        'Settings → Privacy → Blocked Users allows unblocking. This does not restore friendship.\n'
        'Friends includes incoming/outgoing requests. Search for Zoe to try sending a new request.\n'
        'Profiles, venues and reviews labeled Demo are fictional.\n'
        'Terms: https://trustmap.hubertik.com/terms/ (also on welcome, demo sign-in and Settings → About).\n'
        'These credentials work after the generated settings are applied to the deployed API.\n',
    ]
    for path, content in zip(paths, contents):
        descriptor = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
        with os.fdopen(descriptor, 'w') as file:
            file.write(content)
        print(f'Created {path}')


if __name__ == '__main__':
    main()
