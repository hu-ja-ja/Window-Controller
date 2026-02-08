#Requires AutoHotkey v2.0
#SingleInstance Force
#Warn All, StdOut

#Include %A_ScriptDir%\window_control.ahk
#Include %A_ScriptDir%\gui.ahk

OnError(WC_OnUnhandledError)
OnExit(WC_OnExit)

WC_OnUnhandledError(ex, mode) {
	; 取りこぼし対策: 例外ダイアログとは別に、必ずファイルとStdOutへ出す
	try {
		stamp := FormatTime(A_Now, "yyyy-MM-dd HH:mm:ss")
		msg := "[" stamp "] Unhandled error (mode=" mode ")`n" ex.Message
		try msg .= "`nWhat: " ex.What
		try msg .= "`nFile: " ex.File
		try msg .= "`nLine: " ex.Line
		try msg .= "`nExtra: " ex.Extra
		msg .= "`n"

		logPath := A_ScriptDir "\\config\\__last_error.txt"
		try DirCreate(A_ScriptDir "\\config")
		FileAppend(msg "`n", logPath, "UTF-8")
		try FileAppend(msg, "*", "UTF-8")
	}
	; デフォルトのエラー表示/挙動は維持（ユーザーがウィンドウで見れる）
	return false
}

WC_OnExit(exitReason, exitCode) {
	try {
		stamp := FormatTime(A_Now, "yyyy-MM-dd HH:mm:ss")
		msg := "[" stamp "] OnExit reason=" exitReason " code=" exitCode "`n"
		logPath := A_ScriptDir "\\config\\__exit_log.txt"
		try DirCreate(A_ScriptDir "\\config")
		FileAppend(msg, logPath, "UTF-8")
		try FileAppend(msg, "*", "UTF-8")
	}
}

try {
	global WC := WindowController(A_ScriptDir)
	WC.LoadConfig()
	WC.StartHooksIfEnabled()
} catch as initErr {
	MsgBox("初期化に失敗しました。`n" initErr.Message, "Window-Controller", 0x10)
	ExitApp
}

TraySetIcon("shell32.dll", 44)
A_TrayMenu.Delete()
A_TrayMenu.Add("GUIを開く", (*) => OpenWindowControllerGui(WC))
A_TrayMenu.Add("プロファイルを適用(配置のみ)", (*) => OpenWindowControllerGui(WC, true))
A_TrayMenu.Add()
A_TrayMenu.Add("終了", (*) => ExitApp())

; ホットキー（必要なら変更してください）
^!w::OpenWindowControllerGui(WC) ; Ctrl+Alt+W

; 起動直後にGUIを開く（邪魔なら false に）
if WC.Config["settings"]["showGuiOnStartup"] {
	SetTimer((*) => OpenWindowControllerGui(WC), -50)
}

