"""Read-only admin page listing the most recently registered RustDesk IDs.

Reads hbbs's db_v2.sqlite3 without writing. Password comes from the
ADMIN_PASSWORD environment variable; serve it only behind HTTPS.
"""
import base64
import hmac
import html
import json
import os
import sqlite3
import threading
import time
from datetime import datetime, timedelta, timezone
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

DB_PATH = os.environ.get("HBBS_DB", "/data/db_v2.sqlite3")
KEY_PATH = os.environ.get("HBBS_KEY", os.path.join(os.path.dirname(DB_PATH), "id_ed25519.pub"))
ID_SERVER = os.environ.get("ID_SERVER", "ds307.duckdns.org")
PASSWORD = os.environ.get("ADMIN_PASSWORD", "")
PORT = int(os.environ.get("PORT", "8088"))
LIMIT = 5
MAX_FAILURES = 5
LOCK_SECONDS = 600
KST = timezone(timedelta(hours=9))

failures = {}
failures_lock = threading.Lock()


def recent_peers():
    conn = sqlite3.connect(f"file:{DB_PATH}?mode=ro", uri=True, timeout=5)
    try:
        rows = conn.execute(
            "select id, created_at, info from peer order by created_at desc limit ?", (LIMIT,)
        ).fetchall()
    finally:
        conn.close()
    peers = []
    for peer_id, created_at, info in rows:
        try:
            when = datetime.strptime(created_at, "%Y-%m-%d %H:%M:%S").replace(tzinfo=timezone.utc)
            created = when.astimezone(KST).strftime("%Y-%m-%d %H:%M")
        except (TypeError, ValueError):
            created = str(created_at)
        try:
            ip = json.loads(info).get("ip", "")
        except (TypeError, ValueError, AttributeError):
            ip = ""
        peers.append((peer_id, created, ip.replace("::ffff:", "")))
    return peers


def server_key():
    # hbbs public key; clients need it for manual setup. Not a secret.
    try:
        with open(KEY_PATH, encoding="utf-8") as handle:
            return handle.read().strip()
    except OSError:
        return ""


def page(peers):
    key = server_key()
    key_html = html.escape(key) if key else "키 파일(id_ed25519.pub)을 찾지 못했습니다."
    rows = "".join(
        f"<tr><td class=id>{html.escape(pid)}</td><td>{html.escape(created)}</td><td>{html.escape(ip)}</td></tr>"
        for pid, created, ip in peers
    ) or "<tr><td colspan=3>등록된 PC가 없습니다.</td></tr>"
    return f"""<!doctype html><html lang=ko><head><meta charset=utf-8>
<meta name=viewport content="width=device-width, initial-scale=1"><meta name=robots content=noindex>
<title>최근 등록 ID</title><style>
body{{margin:0;font:16px/1.5 system-ui,"Malgun Gothic",sans-serif;background:#f5f7fb;color:#111827}}
main{{max-width:640px;margin:0 auto;padding:32px 16px}}h1{{font-size:24px;margin:0 0 4px}}
p{{color:#4b5563;margin:0 0 20px}}table{{width:100%;border-collapse:collapse;background:#fff;border-radius:12px;overflow:hidden}}
th,td{{text-align:left;padding:12px 14px;border-bottom:1px solid #e5e7eb}}th{{font-size:13px;color:#6b7280}}
.id{{font-size:20px;font-weight:700;letter-spacing:.04em;font-variant-numeric:tabular-nums}}
h2{{font-size:18px;margin:32px 0 8px}}dl{{margin:0;background:#fff;border-radius:12px;padding:14px}}dt{{font-size:13px;color:#6b7280}}dd{{margin:2px 0 12px}}dd:last-child{{margin-bottom:0}}
code{{font:15px ui-monospace,Consolas,monospace;word-break:break-all;user-select:all;cursor:text}}
@media(prefers-color-scheme:dark){{body{{background:#0f172a;color:#e5e7eb}}table,dl{{background:#1e293b}}th,td{{border-color:#334155}}p,th,dt{{color:#94a3b8}}}}
</style></head><body><main><h1>최근 등록된 RustDesk ID</h1>
<p>ds307 서버에 처음 등록된 순서로 최근 {LIMIT}대입니다. 시간은 한국 시간입니다.</p>
<table><thead><tr><th>ID</th><th>등록 시각</th><th>접속 IP</th></tr></thead><tbody>{rows}</tbody></table>
<h2>외부 PC에 직접 설정할 때</h2><p>RustDesk 설정 &gt; 네트워크 &gt; ID/릴레이 서버에 입력합니다. 값을 한 번 누르면 전체가 선택됩니다.</p>
<dl><dt>ID 서버</dt><dd><code>{html.escape(ID_SERVER)}</code></dd><dt>Key</dt><dd><code>{key_html}</code></dd></dl>
</main></body></html>"""


class Handler(BaseHTTPRequestHandler):
    server_version = "recent-ids"
    sys_version = ""

    def client_ip(self):
        # The port is bound to 127.0.0.1, so only the NAS reverse proxy can set this header.
        forwarded = self.headers.get("X-Forwarded-For", "")
        return forwarded.split(",")[-1].strip() or self.client_address[0]

    def send(self, status, body, extra=None):
        data = body.encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "text/html; charset=utf-8")
        self.send_header("Content-Length", str(len(data)))
        self.send_header("Cache-Control", "no-store")
        self.send_header("X-Frame-Options", "DENY")
        self.send_header("X-Content-Type-Options", "nosniff")
        self.send_header("Referrer-Policy", "no-referrer")
        self.send_header("Content-Security-Policy", "default-src 'none'; style-src 'unsafe-inline'")
        for key, value in (extra or {}).items():
            self.send_header(key, value)
        self.end_headers()
        self.wfile.write(data)

    def authorized(self, ip):
        header = self.headers.get("Authorization", "")
        if not header.startswith("Basic "):
            return False
        try:
            _, _, given = base64.b64decode(header[6:]).decode("utf-8").partition(":")
        except (ValueError, UnicodeDecodeError):
            return False
        ok = hmac.compare_digest(given.encode(), PASSWORD.encode())
        with failures_lock:
            if ok:
                failures.pop(ip, None)
            else:
                count, _ = failures.get(ip, (0, 0))
                failures[ip] = (count + 1, time.time())
        return ok

    def do_GET(self):
        if self.path not in ("/", "/index.html"):
            self.send(404, "Not found")
            return
        ip = self.client_ip()
        with failures_lock:
            count, last = failures.get(ip, (0, 0))
            if count >= MAX_FAILURES and time.time() - last < LOCK_SECONDS:
                self.send(429, "비밀번호를 여러 번 틀렸습니다. 10분 후 다시 시도해 주세요.")
                return
            if count >= MAX_FAILURES:
                failures.pop(ip, None)
        if not self.authorized(ip):
            self.send(401, "관리자 비밀번호가 필요합니다.",
                      {"WWW-Authenticate": 'Basic realm="remote-support", charset="UTF-8"'})
            return
        try:
            self.send(200, page(recent_peers()))
        except sqlite3.Error as error:
            self.log_error("database error: %s", error)
            self.send(503, "RustDesk 서버 데이터를 읽지 못했습니다.")


if __name__ == "__main__":
    if len(PASSWORD) < 8 or PASSWORD == "change-me-please":
        raise SystemExit("ADMIN_PASSWORD must be set to a private value of at least 8 characters.")
    ThreadingHTTPServer(("0.0.0.0", PORT), Handler).serve_forever()
