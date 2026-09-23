"""Cross-platform local service launcher. Python 3.10+ with Tk required."""
import json
import os
from pathlib import Path
import plistlib
import queue
import shutil
import signal
import socket
import subprocess
import sys
import threading
import tkinter as tk
from tkinter import filedialog, messagebox, ttk
import webbrowser

ROOT = Path(__file__).resolve().parents[2]
WINDOWS = os.name == "nt"
CONFIG_DIR = (Path(os.environ.get("APPDATA", Path.home())) / "AIVTuberLauncher"
              if WINDOWS else Path.home() / "Library/Application Support/AIVTuberLauncher")
CONFIG_FILE = CONFIG_DIR / "settings.json"
MAC_PREFERENCES = Path.home() / "Library/Preferences/com.aivtuber.launcher.plist"
REGISTRY_PATH = r"Software\AIVTuber\Launcher"
DEFAULTS = {
    "youtube_key": "", "openai_key": "", "video_id": "", "youtube_enabled": False,
    "sovits_dir": "C:/GPT-SoVITS-v3lora-20250228" if WINDOWS else str(Path.home() / "GPT-SoVITS"),
    "sovits_python": "", "stt_python": "", "spire_jar": "",
}


def validate_api_key(name, value):
    value = value.strip()
    if not value:
        return value
    if not value.isascii() or any(character.isspace() for character in value):
        raise ValueError(f"{name}에는 영문/숫자로 된 실제 API 키만 입력하세요. 한글, 공백 또는 줄바꿈이 포함되어 있습니다.")
    return value


def load_keys():
    if WINDOWS:
        import winreg
        try:
            with winreg.OpenKey(winreg.HKEY_CURRENT_USER, REGISTRY_PATH) as key:
                return {
                    "youtube_key": winreg.QueryValueEx(key, "YouTubeApiKey")[0],
                    "openai_key": winreg.QueryValueEx(key, "OpenAIApiKey")[0],
                }
        except (FileNotFoundError, OSError):
            return {}
    try:
        with MAC_PREFERENCES.open("rb") as stream:
            values = plistlib.load(stream)
        return {
            "youtube_key": values.get("YouTubeApiKey", ""),
            "openai_key": values.get("OpenAIApiKey", ""),
        }
    except (FileNotFoundError, OSError, plistlib.InvalidFileException):
        return {}


def save_keys(values):
    if WINDOWS:
        import winreg
        with winreg.CreateKey(winreg.HKEY_CURRENT_USER, REGISTRY_PATH) as key:
            winreg.SetValueEx(key, "YouTubeApiKey", 0, winreg.REG_SZ, values["youtube_key"])
            winreg.SetValueEx(key, "OpenAIApiKey", 0, winreg.REG_SZ, values["openai_key"])
        return
    MAC_PREFERENCES.parent.mkdir(parents=True, exist_ok=True)
    temporary = MAC_PREFERENCES.with_suffix(".tmp")
    fd = os.open(temporary, os.O_WRONLY | os.O_CREAT | os.O_TRUNC, 0o600)
    with os.fdopen(fd, "wb") as stream:
        plistlib.dump({"YouTubeApiKey": values["youtube_key"], "OpenAIApiKey": values["openai_key"]}, stream)
    os.replace(temporary, MAC_PREFERENCES)


def save_settings(values):
    CONFIG_DIR.mkdir(parents=True, exist_ok=True)
    temp = CONFIG_FILE.with_suffix(".tmp")
    fd = os.open(temp, os.O_WRONLY | os.O_CREAT | os.O_TRUNC, 0o600)
    with os.fdopen(fd, "w", encoding="utf-8") as stream:
        public_values = {key: value for key, value in values.items() if key not in ("youtube_key", "openai_key")}
        json.dump(public_values, stream, ensure_ascii=False, indent=2)
    os.replace(temp, CONFIG_FILE)
    save_keys(values)


def python_command(explicit, environment):
    if explicit:
        if not Path(explicit).is_file():
            raise ValueError("Python 실행 파일 경로를 확인하세요: " + explicit)
        return [explicit, "-u"]
    conda = shutil.which("conda")
    for folder in ("miniconda3", "miniforge3", "anaconda3"):
        candidate = Path.home() / folder / ("Scripts/conda.exe" if WINDOWS else "bin/conda")
        if not conda and candidate.is_file():
            conda = str(candidate)
    if not conda:
        raise ValueError("Conda를 찾지 못했습니다. 설정에서 해당 환경의 Python 실행 파일을 선택하세요.")
    return [conda, "run", "--no-capture-output", "-n", environment, "python", "-u"]


def command_for(name, settings):
    openai_key = validate_api_key("OpenAI API Key", settings["openai_key"])
    youtube_key = validate_api_key("YouTube API Key", settings["youtube_key"])
    env = os.environ.copy()
    env.update(OPENAI_API_KEY=openai_key, YouTube__ApiKey=youtube_key,
               YouTube__VideoId=settings["video_id"],
               YouTube__Enabled=str(settings["youtube_enabled"]).lower(),
               AIVTUBER_SERVER_URL="http://127.0.0.1:5050", PYTHONUNBUFFERED="1")
    if name == "web":
        if not openai_key:
            raise ValueError("OpenAI API Key를 입력하고 적용하세요.")
        if settings["youtube_enabled"] and not (youtube_key and settings["video_id"]):
            raise ValueError("YouTube 수집에는 API Key와 방송 Video ID가 필요합니다.")
        return ["dotnet", "run", "--project", str(ROOT / "AIVTuber.Server/AIVTuber.Web/AIVTuber.Web.csproj"),
                "--no-launch-profile", "--urls", "http://127.0.0.1:5050"], ROOT, env
    if name == "sovits":
        folder = Path(settings["sovits_dir"])
        if not (folder / "api_v2.py").is_file():
            raise ValueError("GPT-SoVITS 폴더에 api_v2.py가 없습니다.")
        runtime = folder / "runtime/python.exe"
        executable = settings["sovits_python"] or (str(runtime) if WINDOWS and runtime.is_file() else "")
        return python_command(executable, "GPTSoVits") + ["api_v2.py", "-a", "127.0.0.1", "-p", "9881"], folder, env
    if name == "stt":
        if not WINDOWS:
            env.setdefault("AIVTUBER_STT_DEVICE", "cpu")
            env.setdefault("AIVTUBER_STT_COMPUTE_TYPE", "int8")
        return python_command(settings["stt_python"], "RealtimeSTT") + [str(ROOT / "Tools/RealtimeSTT/stt_bridge.py")], ROOT, env
    jar = Path(settings["spire_jar"])
    if not jar.is_file() or jar.suffix.lower() != ".jar":
        raise ValueError("ModTheSpire.jar 파일을 선택하세요. CommunicationMod 설정도 필요합니다.")
    return ["java", "-jar", str(jar)], jar.parent, env


def stop_process(process):
    if WINDOWS:
        if process.poll() is None:
            subprocess.run(["taskkill", "/PID", str(process.pid), "/T", "/F"],
                           capture_output=True, creationflags=subprocess.CREATE_NO_WINDOW, timeout=15)
    else:
        try:
            os.killpg(process.pid, signal.SIGTERM)
            try:
                process.wait(timeout=5)
            except subprocess.TimeoutExpired:
                pass
            os.killpg(process.pid, signal.SIGKILL)
        except ProcessLookupError:
            pass
    process.wait(timeout=10)


class Launcher(tk.Tk):
    def __init__(self):
        super().__init__()
        self.title("AI VTuber · 방송 실행 관리자")
        self.geometry("850x790")
        self.minsize(740, 650)
        self.events = queue.Queue(maxsize=2000)
        self.processes = {}
        self.stopping = set()
        self.closing = False
        self.settings = DEFAULTS.copy()
        stored_settings = {}
        try:
            stored_settings = json.loads(CONFIG_FILE.read_text(encoding="utf-8"))
            self.settings.update(stored_settings)
        except FileNotFoundError:
            pass
        except (ValueError, OSError) as error:
            messagebox.showwarning("설정 읽기 실패", str(error))
        native_keys = load_keys()
        self.settings.update(native_keys)
        legacy_keys = any(stored_settings.get(key) for key in ("youtube_key", "openai_key"))
        if legacy_keys and not any(native_keys.values()):
            try:
                save_settings(self.settings)
            except OSError as error:
                messagebox.showwarning("키 이전 실패", str(error))
        self.vars = {key: (tk.BooleanVar(value=value) if isinstance(value, bool) else tk.StringVar(value=value))
                     for key, value in self.settings.items()}
        body = ttk.Frame(self, padding=16)
        body.pack(fill="both", expand=True)
        ttk.Label(body, text="AI VTuber 방송 컨트롤", font=("", 20, "bold")).pack(anchor="w")
        ttk.Label(body, text="웹서버 127.0.0.1:5050  ·  GPT-SoVITS 9881  ·  LLM: GPT-6 Luna").pack(anchor="w", pady=(4, 12))
        keys = ttk.LabelFrame(body, text="API 키 / 방송", padding=10)
        keys.pack(fill="x")
        keys.columnconfigure(1, weight=1)
        for row, (key, label) in enumerate((("youtube_key", "YouTube API Key"), ("openai_key", "OpenAI API Key"))):
            ttk.Label(keys, text=label).grid(row=row, column=0, sticky="w", padx=4)
            entry = ttk.Entry(keys, textvariable=self.vars[key], show="*")
            entry.grid(row=row, column=1, sticky="ew", padx=6, pady=4)
            ttk.Button(keys, text="◉ 보기/숨김", command=lambda e=entry: e.configure(show="" if e.cget("show") else "*")).grid(row=row, column=2)
            ttk.Button(keys, text="적용", command=lambda k=key: self.apply([k])).grid(row=row, column=3, padx=4)
        ttk.Label(keys, text="방송 Video ID").grid(row=2, column=0, sticky="w")
        ttk.Entry(keys, textvariable=self.vars["video_id"]).grid(row=2, column=1, sticky="ew", padx=6, pady=4)
        ttk.Checkbutton(keys, text="채팅 수집", variable=self.vars["youtube_enabled"]).grid(row=2, column=2)
        ttk.Button(keys, text="적용", command=lambda: self.apply(["video_id", "youtube_enabled"])).grid(row=2, column=3)
        key_storage = "Windows 사용자 레지스트리" if WINDOWS else "macOS 사용자 환경설정"
        ttk.Label(keys, text=f"키는 {key_storage}에 저장됩니다. 변경 후 웹서버를 재시작하세요.").grid(row=3, column=0, columnspan=4, sticky="w", pady=6)
        paths = ttk.LabelFrame(body, text="실행 경로 (최초 한 번 설정)", padding=10)
        paths.pack(fill="x", pady=10)
        paths.columnconfigure(1, weight=1)
        for row, (key, label) in enumerate((("sovits_dir", "GPT-SoVITS 폴더"), ("sovits_python", "SoVITS Python (선택)"),
                                            ("stt_python", "STT Python (선택)"), ("spire_jar", "ModTheSpire.jar"))):
            ttk.Label(paths, text=label).grid(row=row, column=0, sticky="w")
            ttk.Entry(paths, textvariable=self.vars[key]).grid(row=row, column=1, sticky="ew", padx=6, pady=3)
            ttk.Button(paths, text="찾기", command=lambda k=key: self.browse(k)).grid(row=row, column=2)
        ttk.Button(paths, text="경로 적용", command=lambda: self.apply(["sovits_dir", "sovits_python", "stt_python", "spire_jar"])).grid(row=4, column=2, pady=5)
        controls = ttk.Frame(body)
        controls.pack(fill="x")
        self.buttons, self.status = {}, {}
        for column, (name, label) in enumerate((("web", "웹서버"), ("sovits", "GPT-SoVITS"), ("stt", "RealtimeSTT"), ("spire", "슬더스 (모드)"))):
            controls.columnconfigure(column, weight=1)
            button = ttk.Button(controls, text=label + " 시작", command=lambda n=name: self.toggle(n))
            button.grid(row=0, column=column, sticky="ew", padx=3)
            state = ttk.Label(controls, text="중지됨", anchor="center")
            state.grid(row=1, column=column, pady=5)
            self.buttons[name] = (button, label)
            self.status[name] = state
        ttk.Button(body, text="웹 채팅 열기 (:5050)", command=lambda: webbrowser.open("http://127.0.0.1:5050")).pack(anchor="w", pady=6)
        self.notice = ttk.Label(body, text="Python 경로를 비우면 Conda 환경 GPTSoVits / RealtimeSTT를 사용합니다.")
        self.notice.pack(anchor="w")
        self.log_tabs = ttk.Notebook(body)
        self.log_tabs.pack(fill="both", expand=True, pady=(8, 0))
        self.logs = {}
        for name, label in (("web", "웹서버"), ("sovits", "GPT-SoVITS"),
                            ("stt", "RealtimeSTT"), ("spire", "슬더스")):
            frame = ttk.Frame(self.log_tabs)
            log = tk.Text(frame, height=12, state="disabled", wrap="word")
            scroll = ttk.Scrollbar(frame, orient="vertical", command=log.yview)
            log.configure(yscrollcommand=scroll.set)
            log.pack(side="left", fill="both", expand=True)
            scroll.pack(side="right", fill="y")
            self.bind_console_shortcuts(log)
            self.log_tabs.add(frame, text=label)
            self.logs[name] = log
        self.protocol("WM_DELETE_WINDOW", self.close)
        self.after(100, self.pump)

    def bind_console_shortcuts(self, console):
        console.bind("<Control-a>", lambda _event, widget=console: self.console_select_all(widget))
        console.bind("<Control-c>", lambda _event, widget=console: self.console_copy(widget))
        console.bind("<Control-v>", lambda _event, widget=console: self.console_paste(widget))
        console.bind("<Command-a>", lambda _event, widget=console: self.console_select_all(widget))
        console.bind("<Command-c>", lambda _event, widget=console: self.console_copy(widget))
        console.bind("<Command-v>", lambda _event, widget=console: self.console_paste(widget))

        menu = tk.Menu(console, tearoff=False)
        menu.add_command(label="전체 선택", command=lambda: self.console_select_all(console))
        menu.add_command(label="복사", command=lambda: self.console_copy(console))
        menu.add_command(label="붙여넣기", command=lambda: self.console_paste(console))

        def show_menu(event):
            menu.tk_popup(event.x_root, event.y_root)
            return "break"

        console.bind("<Button-2>", show_menu)
        console.bind("<Button-3>", show_menu)

    @staticmethod
    def console_select_all(console):
        console.tag_add("sel", "1.0", "end-1c")
        console.mark_set("insert", "end-1c")
        console.see("insert")
        return "break"

    def console_copy(self, console):
        try:
            selected = console.get("sel.first", "sel.last")
        except tk.TclError:
            return "break"
        self.clipboard_clear()
        self.clipboard_append(selected)
        self.update_idletasks()
        return "break"

    def console_paste(self, console):
        try:
            pasted = self.clipboard_get()
        except tk.TclError:
            return "break"
        console.configure(state="normal")
        try:
            try:
                console.delete("sel.first", "sel.last")
            except tk.TclError:
                pass
            console.insert("insert", pasted)
            console.see("insert")
        finally:
            console.configure(state="disabled")
        return "break"

    def browse(self, key):
        value = filedialog.askdirectory() if key == "sovits_dir" else filedialog.askopenfilename()
        if value:
            self.vars[key].set(value)

    def apply(self, keys):
        updated = self.settings.copy()
        for key in keys:
            value = self.vars[key].get()
            updated[key] = value.strip() if isinstance(value, str) else value
        try:
            updated["youtube_key"] = validate_api_key("YouTube API Key", updated["youtube_key"])
            updated["openai_key"] = validate_api_key("OpenAI API Key", updated["openai_key"])
            save_settings(updated)
            self.settings = updated
            self.notice.configure(text="적용했습니다. 실행 중인 서비스에는 재시작 후 반영됩니다.")
        except (OSError, ValueError) as error:
            messagebox.showerror("저장 실패", str(error))

    def emit(self, event):
        try:
            self.events.put_nowait(event)
        except queue.Full:
            pass

    def toggle(self, name):
        if name in self.processes:
            self.stop(name)
            return
        try:
            if name in ("web", "sovits"):
                port = 5050 if name == "web" else 9881
                with socket.socket() as sock:
                    if sock.connect_ex(("127.0.0.1", port)) == 0:
                        raise ValueError(f"포트 {port}가 이미 사용 중입니다. 기존 서비스를 먼저 종료하세요.")
            command, cwd, env = command_for(name, self.settings)
            options = {"creationflags": subprocess.CREATE_NO_WINDOW} if WINDOWS else {"start_new_session": True}
            process = subprocess.Popen(command, cwd=cwd, env=env, stdout=subprocess.PIPE,
                                       stderr=subprocess.STDOUT, stdin=subprocess.DEVNULL, **options)
            self.processes[name] = process
            self.log_tabs.select(list(self.logs).index(name))
            self.buttons[name][0].configure(text=self.buttons[name][1] + " 종료")
            self.status[name].configure(text="실행 중 (로그 확인)")
            secrets = [self.settings[k] for k in ("youtube_key", "openai_key") if self.settings[k]]
            threading.Thread(target=self.read_log, args=(name, process, secrets), daemon=True).start()
        except (ValueError, OSError) as error:
            messagebox.showerror("실행 실패", str(error))

    def read_log(self, name, process, secrets):
        with process.stdout:
            for raw in iter(process.stdout.readline, b""):
                line = raw.decode("utf-8", errors="replace")
                for secret in secrets:
                    line = line.replace(secret, "****")
                self.emit(("log", name, line))

    def stop(self, name):
        if name in self.stopping:
            return
        self.stopping.add(name)
        self.buttons[name][0].configure(state="disabled")
        self.status[name].configure(text="종료 중…")
        threading.Thread(target=self.stop_worker, args=(name, self.processes[name]), daemon=True).start()

    def stop_worker(self, name, process):
        try:
            stop_process(process)
        except (OSError, subprocess.SubprocessError) as error:
            self.emit(("log", name, "종료 실패: " + str(error) + "\n"))
        finally:
            self.events.put(("stopped", name, ""))

    def pump(self):
        for _ in range(200):
            try:
                kind, name, text = self.events.get_nowait()
            except queue.Empty:
                break
            if kind == "stopped":
                self.stopping.discard(name)
                process = self.processes.get(name)
                if process is not None and process.poll() is None:
                    self.closing = False
                    self.buttons[name][0].configure(state="normal")
                    self.status[name].configure(text="종료 실패 · 다시 시도")
            else:
                log = self.logs[name]
                log.configure(state="normal")
                log.insert("end", text)
                if int(log.index("end-1c").split(".")[0]) > 1500:
                    log.delete("1.0", "300.0")
                log.see("end")
                log.configure(state="disabled")
        for name, process in list(self.processes.items()):
            if process.poll() is not None and name not in self.stopping:
                del self.processes[name]
                button, label = self.buttons[name]
                button.configure(text=label + " 시작", state="normal")
                self.status[name].configure(text=f"종료됨 (코드 {process.returncode})")
        if self.closing and not self.processes:
            self.destroy()
            return
        self.after(100, self.pump)

    def close(self):
        if self.processes and not messagebox.askyesno("종료", "이 창에서 실행한 서비스와 게임을 종료하고 닫을까요?"):
            return
        self.closing = True
        for name in list(self.processes):
            self.stop(name)


if __name__ == "__main__":
    Launcher().mainloop()
