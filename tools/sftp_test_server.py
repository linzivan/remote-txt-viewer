"""极简 SFTP 测试服务器：127.0.0.1:2222, user=test pass=123456, 根目录 ./testroot"""
import errno
import os
import socket
import sys
import threading
import time

import paramiko

ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "testroot"))
HOST_KEY = paramiko.RSAKey.generate(2048)


def safe(real):
    return real


class MyServer(paramiko.ServerInterface):
    def check_auth_password(self, username, password):
        if username == "test" and password == "123456":
            return paramiko.AUTH_SUCCESSFUL
        return paramiko.AUTH_FAILED

    def check_channel_request(self, kind, chanid):
        if kind == "session":
            return paramiko.OPEN_SUCCEEDED
        return paramiko.OPEN_FAILED_ADMINISTRATIVELY_PROHIBITED

    def get_allowed_auths(self, username):
        return "password"


class MySFTPServer(paramiko.SFTPServerInterface):
    def _real(self, path):
        p = os.path.normpath(os.path.join(ROOT, path.lstrip("/")))
        if not p.startswith(ROOT):
            p = ROOT
        return p

    def list_folder(self, path):
        real = self._real(path)
        out = []
        try:
            for name in os.listdir(real):
                full = os.path.join(real, name)
                try:
                    attr = paramiko.SFTPAttributes.from_stat(os.stat(full))
                except OSError:
                    continue
                attr.filename = name
                out.append(attr)
        except OSError as e:
            return paramiko.SFTPServer.convert_errno(e.errno)
        return out

    def stat(self, path):
        real = self._real(path)
        try:
            return paramiko.SFTPAttributes.from_stat(os.stat(real))
        except OSError as e:
            return paramiko.SFTPServer.convert_errno(e.errno)

    def lstat(self, path):
        return self.stat(path)

    def open(self, path, flags, attr):
        real = self._real(path)
        try:
            if flags & os.O_WRONLY:
                mode = "r+b" if os.path.exists(real) else "wb"
            elif flags & os.O_RDWR:
                mode = "r+b" if os.path.exists(real) else "wb"
            elif flags & os.O_APPEND:
                mode = "ab"
            else:
                mode = "rb"
            f = open(real, mode)
        except OSError as e:
            return paramiko.SFTPServer.convert_errno(e.errno)

        h = paramiko.SFTPHandle(flags)
        h.filename = real
        h.readfile = f
        if flags & (os.O_WRONLY | os.O_RDWR | os.O_APPEND):
            h.writefile = f
        return h

    def canonicalize(self, path):
        if not path.startswith("/"):
            path = "/" + path
        parts = []
        for seg in path.split("/"):
            if seg in ("", "."):
                continue
            if seg == "..":
                if parts:
                    parts.pop()
            else:
                parts.append(seg)
        return "/" + "/".join(parts)


def handle(conn, addr):
    try:
        t = paramiko.Transport(conn)
        t.add_server_key(HOST_KEY)
        t.set_subsystem_handler("sftp", paramiko.SFTPServer, MySFTPServer)
        t.start_server(server=MyServer())
        while t.is_active():
            time.sleep(0.5)
    except Exception as e:
        sys.stderr.write("conn error: %r\n" % (e,))
    finally:
        try:
            conn.close()
        except Exception:
            pass


def main():
    os.makedirs(os.path.join(ROOT, "var", "log"), exist_ok=True)
    s = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    s.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    s.bind(("127.0.0.1", 2222))
    s.listen(10)
    print("SFTP test server on 127.0.0.1:2222  root=%s" % ROOT, flush=True)
    while True:
        conn, addr = s.accept()
        threading.Thread(target=handle, args=(conn, addr), daemon=True).start()


if __name__ == "__main__":
    main()
