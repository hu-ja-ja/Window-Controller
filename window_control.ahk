#Requires AutoHotkey v2.0

#Include %A_ScriptDir%\json.ahk

class WindowController {
	; WinEvent 定数（複数メソッドで共用）
	static EVENT_SYSTEM_FOREGROUND := 0x0003
	static EVENT_SYSTEM_MINIMIZESTART := 0x0016
	static EVENT_SYSTEM_MINIMIZEEND := 0x0017
	static EVENT_OBJECT_STATECHANGE := 0x800A
	static WINEVENT_OUTOFCONTEXT := 0x0000
	static WINEVENT_SKIPOWNPROCESS := 0x0002

	__New(scriptDir) {
		this.ScriptDir := scriptDir
		this.ConfigDir := scriptDir "\\config"
		this.ProfilesPath := this.ConfigDir "\\profiles.json"
		this.LogPath := this.ConfigDir "\\window-controller.log"

		this.Config := Map(
			"version", 1,
			"settings", Map(
				"syncMinMax", true,
				"showGuiOnStartup", true
			),
			"profiles", []
		)

		this.ActiveProfileName := ""
		this.ActiveGroupHwnds := Map() ; hwndStr -> true
		this._isPropagating := false
		this._hookHandles := []
		this._hookCallback := 0
		this._lastMinMaxByHwnd := Map() ; hwndStr -> -1/0/1
		this.SyncProfileGroups := Map() ; profileName -> Map(hwndStr->true)
		this._lastSyncGroupRebuildTick := 0
		this._lastForegroundHwndByProfile := Map() ; profileName -> hwndStr
		this._lastForegroundTickByProfile := Map() ; profileName -> tick
		this._suppressForegroundSync := false
		this._suppressHookEvents := false
		this._isShuttingDown := false
	}

	__Delete() {
		; 終了時のWinEventコールバックで例外が連打されないよう、確実にフックを外す
		try this.Shutdown()
	}

	Shutdown() {
		; 先に抑止を立ててからフック解除（解除中にイベントが飛んでも安全にreturnできる）
		this._isShuttingDown := true
		this._suppressHookEvents := true
		this._suppressForegroundSync := true
		this._RemoveHooks()
	}

	_HasAnySyncProfile() {
		for p in this.Config["profiles"] {
			if (p is Map) && p.Has("syncMinMax") && p["syncMinMax"]
				return true
		}
		return false
	}

	EnsureConfigDir() {
		if !DirExist(this.ConfigDir)
			DirCreate(this.ConfigDir)
	}

	Log(msg) {
		try {
			this.EnsureConfigDir()
			stamp := FormatTime(A_Now, "yyyy-MM-dd HH:mm:ss")
			FileAppend(stamp "  " msg "`n", this.LogPath, "UTF-8")
		}
	}

	LoadConfig() {
		this.EnsureConfigDir()

		if !FileExist(this.ProfilesPath) {
			this.SaveConfig()
			return
		}

		try {
			txt := FileRead(this.ProfilesPath, "UTF-8")
			obj := WcJson.Parse(txt)
			this.Config := this._NormalizeConfig(obj)
			this.RebuildSyncGroups()
		} catch as ex {
			this.Log("LoadConfig failed: " ex.Message)
			try {
				bak := this.ProfilesPath ".broken." FormatTime(A_Now, "yyyyMMdd_HHmmss")
				FileMove(this.ProfilesPath, bak, 1)
			}
			this.Config := Map(
				"version", 1,
				"settings", Map("syncMinMax", true, "showGuiOnStartup", true),
				"profiles", []
			)
			this.SaveConfig()
			this.RebuildSyncGroups()
		}
	}

	RebuildSyncGroups() {
		; syncMinMax=true のプロファイルごとに、現在のウィンドウHWND集合を解決して保持
		this.SyncProfileGroups := Map()
		this._lastSyncGroupRebuildTick := A_TickCount
		for p in this.Config["profiles"] {
			if !(p is Map) || !p.Has("name")
				continue
			if !p.Has("syncMinMax") || !p["syncMinMax"]
				continue
			group := Map()
			if p.Has("windows") && (p["windows"] is Array) {
				for entry in p["windows"] {
					try {
						hwnd := this._FindWindowForEntry(entry)
						if hwnd && WinExist("ahk_id " hwnd)
							group[String(hwnd)] := true
					}
				}
			}
			this.SyncProfileGroups[p["name"]] := group
		}
	}

	_NormalizeConfig(obj) {
		cfg := Map(
			"version", 1,
			"settings", Map("syncMinMax", true, "showGuiOnStartup", true),
			"profiles", []
		)
		if (obj is Map) {
			if obj.Has("version")
				cfg["version"] := obj["version"]
			if obj.Has("settings") && (obj["settings"] is Map) {
				s := obj["settings"]
				if s.Has("syncMinMax")
					cfg["settings"]["syncMinMax"] := !!s["syncMinMax"]
				if s.Has("showGuiOnStartup")
					cfg["settings"]["showGuiOnStartup"] := !!s["showGuiOnStartup"]
			}
			if obj.Has("profiles") && (obj["profiles"] is Array) {
				; 既存データにも互換のためデフォルト値を補完
				normProfiles := []
				for p in obj["profiles"] {
					if (p is Map) {
						if !p.Has("syncMinMax")
							p["syncMinMax"] := false
						; path の \\ 増殖をここで正規化（過去バグの後処理）
						if p.Has("windows") && (p["windows"] is Array) {
							for entry in p["windows"] {
								if (entry is Map) {
									if entry.Has("path")
										entry["path"] := this._NormalizeWindowsPath(entry["path"])
								}
							}
						}
					}
					normProfiles.Push(p)
				}
				cfg["profiles"] := normProfiles
			}
		}
		return cfg
	}

	_NormalizeWindowsPath(p) {
		try p := String(p)
		catch
			return p
		if (p = "")
			return p

		; Drive path: C:\\foo -> C:\foo
		if RegExMatch(p, "i)^[a-z]:\\\\") {
			while InStr(p, "\\\\")
				p := StrReplace(p, "\\\\", "\\")
			return p
		}
		; UNC: \\server\\share -> \\server\share （先頭2本は維持）
		if (SubStr(p, 1, 2) = "\\\\") {
			head := "\\\\"
			tail := SubStr(p, 3)
			while InStr(tail, "\\\\")
				tail := StrReplace(tail, "\\\\", "\\")
			return head tail
		}
		return p
	}

	SaveConfig() {
		try {
			this.EnsureConfigDir()
			json := WcJson.Stringify(this.Config, true)
			tmp := this.ProfilesPath ".tmp"
			try FileDelete(tmp)
			FileAppend(json, tmp, "UTF-8")
			FileMove(tmp, this.ProfilesPath, 1)
		} catch as ex {
			this.Log("SaveConfig failed: " ex.Message)
			throw ex
		}
	}

	GetProfileNames() {
		names := []
		for p in this.Config["profiles"] {
			if (p is Map) && p.Has("name")
				names.Push(p["name"])
		}
		return names
	}

	FindProfileByName(name) {
		for idx, p in this.Config["profiles"] {
			if (p is Map) && p.Has("name") && (p["name"] = name)
				return Map("index", idx, "profile", p)
		}
		return 0
	}

	DeleteProfile(name) {
		hit := this.FindProfileByName(name)
		if !hit
			return false
		this.Config["profiles"].RemoveAt(hit["index"])
		if (this.ActiveProfileName = name) {
			this.ActiveProfileName := ""
			this.ActiveGroupHwnds := Map()
		}
		this.SaveConfig()
		this.RebuildSyncGroups()
		return true
	}

	SetSyncMinMax(enabled) {
		this.Config["settings"]["syncMinMax"] := !!enabled
		this.SaveConfig()
		this.StartHooksIfEnabled()
	}

	SetShowGuiOnStartup(enabled) {
		this.Config["settings"]["showGuiOnStartup"] := !!enabled
		this.SaveConfig()
	}

	StartHooksIfEnabled() {
		if this.Config["settings"]["syncMinMax"] && this._HasAnySyncProfile()
			this._EnsureHooks()
		else
			this._RemoveHooks()
	}

	_EnsureHooks() {
		if (this._hookHandles.Length > 0)
			return
		this._hookCallback := CallbackCreate(ObjBindMethod(this, "_OnWinEvent"), "Fast", 7)
		flags := (WindowController.WINEVENT_OUTOFCONTEXT | WindowController.WINEVENT_SKIPOWNPROCESS)

		; NOTE: 必要なイベントだけを個別にフックする（巨大レンジ指定は不安定）
		h1 := DllCall("SetWinEventHook"
			, "UInt", WindowController.EVENT_SYSTEM_MINIMIZESTART
			, "UInt", WindowController.EVENT_SYSTEM_MINIMIZEEND
			, "Ptr", 0
			, "Ptr", this._hookCallback
			, "UInt", 0
			, "UInt", 0
			, "UInt", flags
			, "Ptr")
		if h1
			this._hookHandles.Push(h1)
		else
			this.Log("SetWinEventHook(minimize) failed: " A_LastError)

		h2 := DllCall("SetWinEventHook"
			, "UInt", WindowController.EVENT_OBJECT_STATECHANGE
			, "UInt", WindowController.EVENT_OBJECT_STATECHANGE
			, "Ptr", 0
			, "Ptr", this._hookCallback
			, "UInt", 0
			, "UInt", 0
			, "UInt", flags
			, "Ptr")
		if h2
			this._hookHandles.Push(h2)
		else
			this.Log("SetWinEventHook(statechange) failed: " A_LastError)

		h3 := DllCall("SetWinEventHook"
			, "UInt", WindowController.EVENT_SYSTEM_FOREGROUND
			, "UInt", WindowController.EVENT_SYSTEM_FOREGROUND
			, "Ptr", 0
			, "Ptr", this._hookCallback
			, "UInt", 0
			, "UInt", 0
			, "UInt", flags
			, "Ptr")
		if h3
			this._hookHandles.Push(h3)
		else
			this.Log("SetWinEventHook(foreground) failed: " A_LastError)
	}

	_RemoveHooks() {
		for h in this._hookHandles {
			try DllCall("UnhookWinEvent", "Ptr", h)
		}
		this._hookHandles := []
		this._lastMinMaxByHwnd := Map()
		this._lastForegroundHwndByProfile := Map()
		this._lastForegroundTickByProfile := Map()
		if this._hookCallback {
			try CallbackFree(this._hookCallback)
		}
		this._hookCallback := 0
	}

	_OnWinEvent(hWinEventHook, event, hwnd, idObject, idChild, dwEventThread, dwmsEventTime) {
		try {
			if this._isShuttingDown
				return
			if (idObject != 0) || (idChild != 0)
				return
			if !hwnd
				return
			if !this.Config["settings"]["syncMinMax"]
				return
			if !this._HasAnySyncProfile()
				return
			if this._suppressHookEvents
				return
			if this._isPropagating
				return

			if (event = WindowController.EVENT_SYSTEM_FOREGROUND) {
				if this._suppressForegroundSync
					return
				this._OnForegroundEvent(hwnd)
				return
			}

			mm := 0
			try mm := WinGetMinMax("ahk_id " hwnd)
			catch {
				return
			}
			hwndKey := String(hwnd)
			if this._lastMinMaxByHwnd.Has(hwndKey) {
				if (this._lastMinMaxByHwnd[hwndKey] = mm)
					return
			}
			this._lastMinMaxByHwnd[hwndKey] := mm

			groups := this._GetSyncGroupsContainingHwnd(hwndKey)
			if (groups.Length = 0) {
				; HWNDが変わった/ウィンドウが作り直された場合に追随
				if (A_TickCount - this._lastSyncGroupRebuildTick) > 1200 {
					this.RebuildSyncGroups()
					groups := this._GetSyncGroupsContainingHwnd(hwndKey)
				}
			}
			if (groups.Length = 0)
				return

			this._isPropagating := true
			try {
				for g in groups {
					this._PropagateMinMaxWithinGroup(g, hwnd, mm)
				}
			} finally {
				this._isPropagating := false
			}
		} catch as ex {
			; WinEventコールバック内の例外は未処理だとダイアログ連打になるため、握りつぶす
			try {
				if !this._isShuttingDown
					this.Log("OnWinEvent error: " ex.Message)
			}
			return
		}
	}

	_OnForegroundEvent(hwnd) {
		; 最小化中にフォーカスが移った時の前面イベントで暴れないよう、最小化中は無視
		try {
			if (WinGetMinMax("ahk_id " hwnd) = -1)
				return
		} catch {
			return
		}

		hwndKey := String(hwnd)
		groups := this._GetSyncGroupsContainingHwnd(hwndKey)
		if (groups.Length = 0) {
			if (A_TickCount - this._lastSyncGroupRebuildTick) > 1200 {
				this.RebuildSyncGroups()
				groups := this._GetSyncGroupsContainingHwnd(hwndKey)
			}
		}
		if (groups.Length = 0)
			return

		this._isPropagating := true
		try {
			for g in groups {
				this._PropagateForegroundWithinGroup(g, hwnd)
			}
		} finally {
			this._isPropagating := false
		}
	}

	_PropagateForegroundWithinGroup(groupObj, sourceHwnd) {
		name := groupObj["name"]
		now := A_TickCount
		if this._lastForegroundTickByProfile.Has(name) {
			if (now - this._lastForegroundTickByProfile[name] < 250) && this._lastForegroundHwndByProfile.Has(name) {
				if (this._lastForegroundHwndByProfile[name] = String(sourceHwnd))
					return
			}
		}
		this._lastForegroundTickByProfile[name] := now
		this._lastForegroundHwndByProfile[name] := String(sourceHwnd)

		group := groupObj["group"]
		count := 0
		for targetKey, _ in group {
			target := Integer(targetKey)
			if (target = sourceHwnd)
				continue
			if !WinExist("ahk_id " target)
				continue
			try {
				; 最小化中のウィンドウは、前面同期では復元しない（最小化操作と干渉しやすい）
				try {
					if (WinGetMinMax("ahk_id " target) = -1)
						continue
				} catch {
					continue
				}
				; 元ウィンドウの直後（背面側）に配置して、フォーカスは奪わずに前面へ
				SWP_NOSIZE := 0x0001
				SWP_NOMOVE := 0x0002
				SWP_NOACTIVATE := 0x0010
				DllCall("SetWindowPos"
					, "Ptr", target
					, "Ptr", sourceHwnd
					, "Int", 0, "Int", 0, "Int", 0, "Int", 0
					, "UInt", (SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE))
				count += 1
			}
		}
		if (count > 0)
			this.Log("Foreground sync within profile '" name "' to " count " window(s)")
	}

	_GetSyncGroupsContainingHwnd(hwndKey) {
		hits := []
		for name, group in this.SyncProfileGroups {
			if (group is Map) && group.Has(hwndKey)
				hits.Push(Map("name", name, "group", group))
		}
		return hits
	}

	_PropagateMinMaxWithinGroup(groupObj, sourceHwnd, mm) {
		group := groupObj["group"]
		count := 0
		for targetKey, _ in group {
			target := Integer(targetKey)
			if (target = sourceHwnd)
				continue
			if !WinExist("ahk_id " target)
				continue
			try {
				if (mm = -1) {
					WinMinimize("ahk_id " target)
				} else if (mm = 1) {
					; 最小化中のウィンドウは Restore を挟んだ方が安定する
					try WinRestore("ahk_id " target)
					Sleep(20)
					WinMaximize("ahk_id " target)
				} else {
					WinRestore("ahk_id " target)
				}
				; 伝播で変えた分はキャッシュも更新しておく（イベントは _isPropagating で捨てられるため）
				this._lastMinMaxByHwnd[String(target)] := mm
				count += 1
			}
		}
		if (count > 0)
			this.Log("Sync propagated within profile '" groupObj["name"] "' to " count " window(s)")
	}

	_CollectHwndsForProfile(profile) {
		set := Map() ; hwndStr -> true
		if !(profile is Map) || !profile.Has("windows") || !(profile["windows"] is Array)
			return set

		; exe/class の組み合わせ単位でWinGetListを叩いて集合化（タイトル変動でも安定）
		seenKeys := Map()
		for entry in profile["windows"] {
			if !(entry is Map) || !entry.Has("match") || !(entry["match"] is Map)
				continue
			m := entry["match"]
			exe := m.Has("exe") ? m["exe"] : ""
			cls := m.Has("class") ? m["class"] : ""
			if (exe = "")
				continue
			key := StrLower(exe) "|" cls
			if seenKeys.Has(key)
				continue
			seenKeys[key] := true

			try {
				hwnds := WinGetList("ahk_exe " exe)
				for hwnd in hwnds {
					if !WinExist("ahk_id " hwnd)
						continue
					if (cls != "") {
						try {
							if (WinGetClass("ahk_id " hwnd) != cls)
								continue
						} catch {
							continue
						}
					}
					set[String(hwnd)] := true
				}
			} catch {
				; ignore
			}
		}
		return set
	}

	EnumerateWindows() {
		wins := []
		hwnds := []
		try hwnds := WinGetList()
		catch as ex {
			this.Log("WinGetList failed: " ex.Message)
			return wins
		}

		for hwnd in hwnds {
			try {
				if !WinExist("ahk_id " hwnd)
					continue
				title := WinGetTitle("ahk_id " hwnd)
				if (title = "")
					continue
				style := WinGetStyle("ahk_id " hwnd)
				exStyle := WinGetExStyle("ahk_id " hwnd)
				WS_VISIBLE := 0x10000000
				WS_EX_TOOLWINDOW := 0x00000080
				if !(style & WS_VISIBLE)
					continue
				if (exStyle & WS_EX_TOOLWINDOW)
					continue

				exe := WinGetProcessName("ahk_id " hwnd)
				cls := WinGetClass("ahk_id " hwnd)
				path := ""
				try path := WinGetProcessPath("ahk_id " hwnd)
				url := ""
				try url := this.TryGetBrowserUrl(hwnd, exe, false)

				; ブラウザ識別情報（プロファイル等）
				browserInfo := ""
				exeLower := StrLower(exe)
				if (this._IsBrowserExe(exeLower) != "") {
					try {
						pid := WinGetPID("ahk_id " hwnd)
						if pid {
							cmd := this._GetProcessCommandLine(pid)
							bident := this._ExtractBrowserIdentity(exeLower, cmd)
							if (bident is Map) && (bident.Count > 0) {
								; GUI表示用に要約
								parts := []
								if bident.Has("profileDirectory")
									parts.Push(bident["profileDirectory"])
								else if bident.Has("profileName")
									parts.Push(bident["profileName"])
								else if bident.Has("profileDir")
									parts.Push(bident["profileDir"])
								if (parts.Length > 0)
									browserInfo := parts[1]
							}
						}
					}
				}

				wins.Push(Map(
					"hwnd", hwnd,
					"title", title,
					"exe", exe,
					"class", cls,
					"path", path,
					"url", url,
					"browserProfile", browserInfo
				))
			} catch as ex {
				this.Log("EnumerateWindows item failed: " ex.Message)
			}
		}
		return wins
	}

	; ---- URL正規化・ブラウザ識別ヘルパ群 ----

	_NormalizeUrlForMatch(url) {
		try url := Trim(String(url))
		catch
			return ""
		if (url = "")
			return ""
		url := RegExReplace(url, "\s+", " ")
		; query / fragment は揺れやすいので除去
		url := RegExReplace(url, "[#?].*$", "")
		; scheme://host/path を抽出
		if RegExMatch(url, "i)^(https?|ws|wss|ftp)://([^/]+)(/.*)?$", &m) {
			scheme := StrLower(m[1])
			host := StrLower(m[2])
			path := m[3] ? m[3] : "/"
			return scheme "://" host path
		}
		if RegExMatch(url, "i)^(about:[^ ]+)", &m2)
			return StrLower(m2[1])
		if RegExMatch(url, "i)^file:/{0,3}(.+)$", &m3)
			return "file:" StrLower(m3[1])
		return StrLower(url)
	}

	_GetUrlHost(urlKey) {
		if (urlKey = "")
			return ""
		if RegExMatch(urlKey, "i)^(https?|ws|wss|ftp)://([^/]+)", &m)
			return StrLower(m[2])
		return ""
	}

	_GetProcessCommandLine(pid) {
		try {
			wmi := ComObjGet("winmgmts:")
			q := wmi.ExecQuery("Select CommandLine from Win32_Process where ProcessId=" pid)
			for p in q {
				try return String(p.CommandLine)
			}
		} catch {
		}
		return ""
	}

	_TryGetCmdArg(cmd, keyPattern) {
		if (cmd = "")
			return ""
		; = 形式: --key="value" or --key=value
		if RegExMatch(cmd, "i)(?:^|\s)(" keyPattern ")=(`"[^`"]+`"|\S+)", &m) {
			val := m[2]
			return Trim(val, '`"')
		}
		; 空白区切り: --key "value" or --key value
		if RegExMatch(cmd, "i)(?:^|\s)(" keyPattern ")\s+(`"[^`"]+`"|\S+)", &m2) {
			val := m2[2]
			return Trim(val, '`"')
		}
		return ""
	}

	_IsBrowserExe(exeLower) {
		switch exeLower {
			case "chrome.exe", "msedge.exe", "brave.exe", "vivaldi.exe":
				return "chromium"
			case "firefox.exe", "floorp.exe":
				return "firefox"
			default:
				return ""
		}
	}

	_ExtractBrowserIdentity(exeLower, cmdLine) {
		ident := Map()
		if (cmdLine = "")
			return ident
		kind := this._IsBrowserExe(exeLower)
		if (kind = "chromium") {
			ud := this._TryGetCmdArg(cmdLine, "--user-data-dir")
			pd := this._TryGetCmdArg(cmdLine, "--profile-directory")
			if (ud != "")
				ident["userDataDir"] := this._NormalizeWindowsPath(ud)
			if (pd != "")
				ident["profileDirectory"] := pd
			if (ident.Count > 0)
				ident["kind"] := "chromium"
			return ident
		}
		if (kind = "firefox") {
			profDir := this._TryGetCmdArg(cmdLine, "-profile")
			profName := this._TryGetCmdArg(cmdLine, "-P")
			if (profDir != "")
				ident["profileDir"] := this._NormalizeWindowsPath(profDir)
			if (profName != "")
				ident["profileName"] := profName
			if (ident.Count > 0)
				ident["kind"] := "firefox"
			return ident
		}
		return ident
	}

	TryGetBrowserUrl(hwnd, exe, allowIntrusive := false, batchCtx := 0) {
		exeLower := StrLower(exe)
		isChromium := (exeLower = "chrome.exe") || (exeLower = "msedge.exe") || (exeLower = "brave.exe") || (exeLower = "vivaldi.exe")
		if isChromium {
			try {
				txt := ControlGetText("Chrome_OmniboxView1", "ahk_id " hwnd)
				if (txt != "")
					return txt
			} catch {
			}
			return ""
		}

		isFirefox := (exeLower = "firefox.exe") || (exeLower = "floorp.exe")
		if isFirefox {
			if !allowIntrusive
				return ""
			url := this._TryGetUrlViaClipboard(hwnd, batchCtx)
			if (url = "") {
				try {
					t := WinGetTitle("ahk_id " hwnd)
					this.Log("URL capture failed (firefox) title='" t "'")
				}
			}
			return url
		}
		return ""
	}

	_TryGetUrlViaClipboard(hwnd, batchCtx := 0) {
		; Firefox/Floorp: アドレスバーの直接取得が難しいため、保存時のみクリップボード経由で取得（ベストエフォート）
		try {
			if !WinExist("ahk_id " hwnd)
				return ""

			oldFg := this._suppressForegroundSync
			oldHooks := this._suppressHookEvents
			this._suppressForegroundSync := true
			this._suppressHookEvents := true

			wasMin := false
			try {
				wasMin := (WinGetMinMax("ahk_id " hwnd) = -1)
			} catch {
				wasMin := false
			}
			if wasMin {
				; 最小化中は一時的に復元してURL取得（取得後に元へ戻す）
				try DllCall("ShowWindow", "Ptr", hwnd, "Int", 9)
				Sleep(60)
			}

			isBatch := (batchCtx is Map)
			prevActive := 0
			clipSaved := ""
			if isBatch {
				if batchCtx.Has("origActive")
					prevActive := batchCtx["origActive"]
				if batchCtx.Has("clipSaved")
					clipSaved := batchCtx["clipSaved"]
			} else {
				try prevActive := WinGetID("A")
				clipSaved := ClipboardAll()
			}
			A_Clipboard := ""

			WinActivate("ahk_id " hwnd)
			if !WinWaitActive("ahk_id " hwnd, , 1.2)
				return ""

			; 3回までリトライ（フォーカス移動や描画遅延対策）
			Loop 3 {
				A_Clipboard := ""
				SendEvent("^l")
				Sleep(90)
				SendEvent("^c")
				if ClipWait(1.0) {
					url := Trim(A_Clipboard)
					if (url != "")
						return url
				}
				Sleep(140)
			}
			return ""
		} catch {
			return ""
		} finally {
			; 元の状態へ
			try {
				if wasMin
					DllCall("ShowWindow", "Ptr", hwnd, "Int", 6)
			}
			this._suppressForegroundSync := oldFg
			this._suppressHookEvents := oldHooks
			if !(batchCtx is Map) {
				try A_Clipboard := clipSaved
				try {
					if prevActive && WinExist("ahk_id " prevActive)
						WinActivate("ahk_id " prevActive)
				}
			}
		}
	}

	CaptureProfileFromHwnds(name, hwndsArray) {
		if (Trim(name) = "")
			throw Error("プロファイル名が空です")
		windows := []
		batchCtx := 0
		try {
			batchCtx := Map("origActive", 0, "clipSaved", ClipboardAll())
			try batchCtx["origActive"] := WinGetID("A")
		} catch {
			batchCtx := 0
		}
		for hwnd in hwndsArray {
			try {
				if !WinExist("ahk_id " hwnd)
					continue
				exe := WinGetProcessName("ahk_id " hwnd)
				cls := WinGetClass("ahk_id " hwnd)
				cls := this._NormalizeClassForMatch(cls)
				title := WinGetTitle("ahk_id " hwnd)
				path := ""
				try path := WinGetProcessPath("ahk_id " hwnd)
				url := ""
				try url := this.TryGetBrowserUrl(hwnd, exe, true, batchCtx)

				; URL正規化キー
				urlKey := ""
				try urlKey := this._NormalizeUrlForMatch(url)

				; ブラウザ識別情報（プロファイル等）
				bident := Map()
				exeLower := StrLower(exe)
				if (this._IsBrowserExe(exeLower) != "") {
					try {
						pid := WinGetPID("ahk_id " hwnd)
						if pid {
							cmd := this._GetProcessCommandLine(pid)
							bident := this._ExtractBrowserIdentity(exeLower, cmd)
						}
					}
				}

				x := y := w := h := 0
				WinGetPos(&x, &y, &w, &h, "ahk_id " hwnd)
				mm := WinGetMinMax("ahk_id " hwnd)

				snap := ""
				mon := 0
				if (mm = 0) {
					mon := this._GetMonitorForRect(x, y, w, h)
					if mon {
						snap := this._DetectSnap(x, y, w, h, mon["wa"])
					}
				}

				matchMap := Map(
					"exe", exe,
					"class", cls,
					"title", title,
					"url", url,
					"urlKey", urlKey
				)
				if (bident is Map) && (bident.Count > 0)
					matchMap["browser"] := bident

				entry := Map(
					"match", matchMap,
					"path", path,
					"rect", Map("x", x, "y", y, "w", w, "h", h),
					"minMax", mm
				)
				if (snap != "") {
					entry["snap"] := Map("type", snap)
					if mon {
						entry["monitor"] := Map(
							"index", mon["index"],
							"name", mon["name"]
						)
					}
				}
				windows.Push(entry)
			} catch as ex {
				this.Log("CaptureProfileFromHwnds failed: " ex.Message)
			}
		}
		if (windows.Length = 0)
			throw Error("保存できるウィンドウがありません")

		nowIso := this._NowIso()
		hit := this.FindProfileByName(name)
		if hit {
			prof := hit["profile"]
			; 既存の連動フラグは維持
			if !prof.Has("syncMinMax")
				prof["syncMinMax"] := false
			prof["windows"] := windows
			prof["updatedAt"] := nowIso
			this.Config["profiles"][hit["index"]] := prof
		} else {
			prof := Map(
				"name", name,
				"syncMinMax", false,
				"windows", windows,
				"createdAt", nowIso,
				"updatedAt", nowIso
			)
			this.Config["profiles"].Push(prof)
		}
		this.SaveConfig()
		this.RebuildSyncGroups()
		; バッチで退避したフォーカス/クリップボードを最後に戻す
		if (batchCtx is Map) {
			try A_Clipboard := batchCtx["clipSaved"]
			try {
				orig := batchCtx.Has("origActive") ? batchCtx["origActive"] : 0
				if orig && WinExist("ahk_id " orig)
					WinActivate("ahk_id " orig)
			}
		}
		return true
	}

	GetProfilesForGui() {
		items := []
		for p in this.Config["profiles"] {
			if !(p is Map) || !p.Has("name")
				continue
			sync := (p.Has("syncMinMax") ? !!p["syncMinMax"] : false)
			cnt := (p.Has("windows") && (p["windows"] is Array)) ? p["windows"].Length : 0
			items.Push(Map("name", p["name"], "syncMinMax", sync, "count", cnt))
		}
		return items
	}

	SetProfileSyncMinMax(name, enabled) {
		hit := this.FindProfileByName(name)
		if !hit
			return false
		prof := hit["profile"]
		prof["syncMinMax"] := !!enabled
		this.Config["profiles"][hit["index"]] := prof
		this.SaveConfig()
		this.RebuildSyncGroups()
		this.StartHooksIfEnabled()
		return true
	}

	_NowIso() {
		return FormatTime(A_Now, "yyyy-MM-dd'T'HH:mm:ss")
	}

	ApplyProfile(name, launchMissing := false) {
		hit := this.FindProfileByName(name)
		if !hit
			throw Error("プロファイルが見つかりません: " name)
		prof := hit["profile"]
		if !prof.Has("windows") || !(prof["windows"] is Array)
			throw Error("プロファイルが壊れています")
		entries := prof["windows"]
		appliedHwnds := []
		failures := []

		for entry in entries {
			try {
				hwnd := this._FindWindowForEntry(entry, true)
				if (!hwnd || !WinExist("ahk_id " hwnd)) {
					if launchMissing
						hwnd := this._LaunchAndWait(entry)
				}
				if (!hwnd || !WinExist("ahk_id " hwnd)) {
					failures.Push(this._EntryLabel(entry) " : 見つかりません")
					continue
				}
				this._ArrangeWindow(hwnd, entry)
				appliedHwnds.Push(hwnd)
			} catch as ex {
				failures.Push(this._EntryLabel(entry) " : " ex.Message)
				this.Log("ApplyProfile item failed: " ex.Message)
			}
		}

		this.ActiveProfileName := name
		this.ActiveGroupHwnds := Map()
		for hwnd in appliedHwnds
			this.ActiveGroupHwnds[String(hwnd)] := true

		return Map(
			"appliedCount", appliedHwnds.Length,
			"totalCount", entries.Length,
			"failures", failures
		)
	}

	_EntryLabel(entry) {
		try {
			if entry.Has("match") && (entry["match"] is Map) {
				m := entry["match"]
				exe := m.Has("exe") ? m["exe"] : ""
				title := m.Has("title") ? m["title"] : ""
				return exe " | " title
			}
		}
		return "(unknown)"
	}

	_NormalizeClassForMatch(cls) {
		if (cls = "")
			return ""
		try {
			; Avalonia はクラス名にGUIDが入って起動ごとに変わることがあるため、プレフィックスマッチに落とす
			if RegExMatch(cls, "i)^Avalonia-[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$")
				return "Avalonia-*"
		} catch {
			; ignore
		}

		; WPF系(HwndWrapper[...])も変動しやすいのでプレフィックスマッチに
		if (InStr(cls, "HwndWrapper[") = 1)
			return "HwndWrapper[*"
		return cls
	}

	_ClassMatches(actual, expected) {
		if (expected = "")
			return true
		; 既存データ互換: Avalonia-xxxx... のように保存されている場合もAvaloniaファミリとして扱う
		if (InStr(expected, "Avalonia-") = 1)
			return (InStr(actual, "Avalonia-") = 1)
		if (SubStr(expected, -1) = "*") {
			prefix := SubStr(expected, 1, StrLen(expected) - 1)
			return (InStr(actual, prefix) = 1)
		}
		return (actual = expected)
	}

	_FindWindowForEntry(entry, allowIntrusive := false) {
		if !(entry is Map)
			return 0
		if !entry.Has("match") || !(entry["match"] is Map)
			return 0
		m := entry["match"]
		exe := m.Has("exe") ? m["exe"] : ""
		cls := m.Has("class") ? m["class"] : ""
		wantTitle := m.Has("title") ? m["title"] : ""
		wantPath := entry.Has("path") ? entry["path"] : ""
		; urlKey があればそれを使い、無ければ url から正規化（旧データ互換）
		wantUrlKey := m.Has("urlKey") ? m["urlKey"] : (m.Has("url") ? this._NormalizeUrlForMatch(m["url"]) : "")
		wantHost := this._GetUrlHost(wantUrlKey)
		wantBrowser := (m.Has("browser") && (m["browser"] is Map)) ? m["browser"] : 0
		if (exe = "")
			return 0

		exeLower := StrLower(exe)
		browserKind := this._IsBrowserExe(exeLower)

		; Phase 1: exe + class で候補抽出（必須条件）
		candidates := []
		try {
			hwnds := WinGetList("ahk_exe " exe)
			for hwnd in hwnds {
				if !WinExist("ahk_id " hwnd)
					continue
				if (cls != "") {
					try {
						if !this._ClassMatches(WinGetClass("ahk_id " hwnd), cls)
							continue
					} catch {
						continue
					}
				}
				candidates.Push(hwnd)
			}
		} catch {
			return 0
		}
		if (candidates.Length = 0)
			return 0
		if (candidates.Length = 1)
			return candidates[1]

		; Phase 2: 各候補に非侵襲でスコア加点
		bestHwnd := candidates[1]
		bestScore := -999999
		scoreByHwnd := Map()

		for hwnd in candidates {
			score := 0

			; path 一致（強い）
			if (wantPath != "") {
				try {
					p := WinGetProcessPath("ahk_id " hwnd)
					if (StrLower(p) = StrLower(wantPath))
						score += 60
				}
			}

			; title 一致
			if (wantTitle != "") {
				try {
					t := WinGetTitle("ahk_id " hwnd)
					if (t = wantTitle)
						score += 30
					else if InStr(t, wantTitle)
						score += 10
				}
			}

			; ブラウザ識別情報（プロファイル等）一致
			if (wantBrowser is Map) {
				try {
					pid := WinGetPID("ahk_id " hwnd)
					cmd := ""
					if pid
						cmd := this._GetProcessCommandLine(pid)
					candIdent := this._ExtractBrowserIdentity(exeLower, cmd)
					; Chromium 系
					if wantBrowser.Has("userDataDir") && candIdent.Has("userDataDir") {
						if (StrLower(wantBrowser["userDataDir"]) = StrLower(candIdent["userDataDir"]))
							score += 70
					}
					if wantBrowser.Has("profileDirectory") && candIdent.Has("profileDirectory") {
						if (wantBrowser["profileDirectory"] = candIdent["profileDirectory"])
							score += 50
					}
					; Firefox 系
					if wantBrowser.Has("profileDir") && candIdent.Has("profileDir") {
						if (StrLower(wantBrowser["profileDir"]) = StrLower(candIdent["profileDir"]))
							score += 70
					}
					if wantBrowser.Has("profileName") && candIdent.Has("profileName") {
						if (wantBrowser["profileName"] = candIdent["profileName"])
							score += 50
					}
				}
			}

			; URL 一致（Chromium は非侵襲で取れる）
			if (wantUrlKey != "") && (browserKind = "chromium") {
				try {
					u := this.TryGetBrowserUrl(hwnd, exe, false)
					uk := this._NormalizeUrlForMatch(u)
					if (uk != "") {
						if (uk = wantUrlKey)
							score += 60
						else if (wantHost != "") && (this._GetUrlHost(uk) = wantHost)
							score += 20
					}
				}
			}

			; アクティブウィンドウ優先（微弱なタイブレーカ）
			try {
				if (WinGetID("A") = hwnd)
					score += 5
			}

			scoreByHwnd[String(hwnd)] := score
			if (score > bestScore) {
				bestScore := score
				bestHwnd := hwnd
			}
		}

		; Phase 3: Firefox/Floorp — 明示的な適用操作時のみ、urlKey があり上位が拮抗している場合に侵襲的URL照合
		if allowIntrusive && (browserKind = "firefox") && (wantUrlKey != "") {
			near := []
			for hwnd in candidates {
				s := scoreByHwnd.Has(String(hwnd)) ? scoreByHwnd[String(hwnd)] : 0
				if (s >= bestScore - 5)
					near.Push(hwnd)
			}
			if (near.Length > 1) {
				for hwnd in near {
					try {
						u := this.TryGetBrowserUrl(hwnd, exe, true)
						uk := this._NormalizeUrlForMatch(u)
						if (uk != "") {
							if (uk = wantUrlKey)
								return hwnd
							if (wantHost != "") && (this._GetUrlHost(uk) = wantHost)
								bestHwnd := hwnd
						}
					}
				}
			}
		}

		return bestHwnd
	}

	_LaunchAndWait(entry) {
		m := entry["match"]
		exe := m.Has("exe") ? m["exe"] : ""
		url := m.Has("url") ? m["url"] : ""
		path := entry.Has("path") ? entry["path"] : ""

		before := Map()
		try {
			for hwnd in WinGetList("ahk_exe " exe)
				before[String(hwnd)] := true
		}

		cmd := (path != "" && FileExist(path)) ? ('"' path '"') : exe
		try {
			if (url != "")
				Run(cmd " " this._QuoteArg(url))
			else
				Run(cmd)
		} catch as ex {
			this.Log("Run failed: " ex.Message)
			return 0
		}

		timeoutMs := 12000
		start := A_TickCount
		while (A_TickCount - start < timeoutMs) {
			try {
				hwnds := WinGetList("ahk_exe " exe)
				for hwnd in hwnds {
					key := String(hwnd)
					if before.Has(key)
						continue
					return hwnd
				}
			}
			Sleep(200)
		}
		return this._FindWindowForEntry(entry)
	}

	_QuoteArg(s) {
		s := StrReplace(s, '"', '\\"')
		return '"' s '"'
	}

	_ArrangeWindow(hwnd, entry) {
		if !(entry is Map)
			return

		; snap がある場合は、保存したモニタ(無ければ現在の主モニタ)のワークエリアから再計算
		if entry.Has("snap") && (entry["snap"] is Map) && entry["snap"].Has("type") {
			snapType := entry["snap"]["type"]
			mon := this._GetMonitorForEntry(entry)
			if mon {
				r2 := this._RectFromSnap(mon["wa"], snapType)
				if r2 {
					x := r2["x"], y := r2["y"], w := r2["w"], h := r2["h"]
					goto _wc_have_rect
				}
			}
		}

		if !entry.Has("rect") || !(entry["rect"] is Map)
			return
		r := entry["rect"]
		x := r.Has("x") ? r["x"] : 0
		y := r.Has("y") ? r["y"] : 0
		w := r.Has("w") ? r["w"] : 800
		h := r.Has("h") ? r["h"] : 600
		_wc_have_rect:
		targetState := entry.Has("minMax") ? entry["minMax"] : 0

		try WinRestore("ahk_id " hwnd)
		Sleep(30)
		try WinMove(x, y, w, h, "ahk_id " hwnd)
		if (targetState = -1) {
			try WinMinimize("ahk_id " hwnd)
		} else if (targetState = 1) {
			try WinMaximize("ahk_id " hwnd)
		}
	}

	_GetMonitorForEntry(entry) {
		idx := 0
		name := ""
		if entry.Has("monitor") && (entry["monitor"] is Map) {
			m := entry["monitor"]
			if m.Has("index")
				idx := m["index"]
			if m.Has("name")
				name := m["name"]
		}

		cnt := MonitorGetCount()
		if (cnt <= 0)
			return 0

		; name 優先で探す
		if (name != "") {
			Loop cnt {
				i := A_Index
				try {
					if (MonitorGetName(i) = name) {
						return this._GetMonitorInfo(i)
					}
				}
			}
		}

		; index が妥当ならそれ
		if (idx >= 1) && (idx <= cnt)
			return this._GetMonitorInfo(idx)

		; フォールバック: 主モニタ
		return this._GetMonitorInfo(1)
	}

	_GetMonitorInfo(index) {
		waL := waT := waR := waB := 0
		MonitorGetWorkArea(index, &waL, &waT, &waR, &waB)
		wa := Map(
			"l", waL,
			"t", waT,
			"r", waR,
			"b", waB,
			"w", waR - waL,
			"h", waB - waT
		)
		monName := ""
		try monName := MonitorGetName(index)
		return Map("index", index, "name", monName, "wa", wa)
	}

	_GetMonitorForRect(x, y, w, h) {
		cx := x + (w // 2)
		cy := y + (h // 2)
		cnt := MonitorGetCount()
		if (cnt <= 0)
			return 0
		best := 0
		Loop cnt {
			i := A_Index
			waL := waT := waR := waB := 0
			MonitorGetWorkArea(i, &waL, &waT, &waR, &waB)
			if (cx >= waL) && (cx < waR) && (cy >= waT) && (cy < waB) {
				best := i
				break
			}
		}
		if !best
			best := 1
		return this._GetMonitorInfo(best)
	}

	_DetectSnap(x, y, w, h, wa) {
		; ワークエリアに対して、左右半分/上下半分/四分割をざっくり判定
		tol := 25
		waL := wa["l"], waT := wa["t"], waW := wa["w"], waH := wa["h"]
		if (waW <= 0) || (waH <= 0)
			return ""
		hw := waW // 2
		hh := waH // 2

		isNear(a, b) => Abs(a - b) <= tol
		isNearSize(a, b) => Abs(a - b) <= (tol + 10)

		; 左右半分
		if isNear(x, waL) && isNear(y, waT) && isNearSize(w, hw) && isNearSize(h, waH)
			return "left"
		if isNear(x + w, waL + waW) && isNear(y, waT) && isNearSize(w, hw) && isNearSize(h, waH)
			return "right"
		; 上下半分
		if isNear(x, waL) && isNear(y, waT) && isNearSize(w, waW) && isNearSize(h, hh)
			return "top"
		if isNear(x, waL) && isNear(y + h, waT + waH) && isNearSize(w, waW) && isNearSize(h, hh)
			return "bottom"
		; 四分割
		if isNear(x, waL) && isNear(y, waT) && isNearSize(w, hw) && isNearSize(h, hh)
			return "topLeft"
		if isNear(x + w, waL + waW) && isNear(y, waT) && isNearSize(w, hw) && isNearSize(h, hh)
			return "topRight"
		if isNear(x, waL) && isNear(y + h, waT + waH) && isNearSize(w, hw) && isNearSize(h, hh)
			return "bottomLeft"
		if isNear(x + w, waL + waW) && isNear(y + h, waT + waH) && isNearSize(w, hw) && isNearSize(h, hh)
			return "bottomRight"

		return ""
	}

	_RectFromSnap(wa, snapType) {
		waL := wa["l"], waT := wa["t"], waW := wa["w"], waH := wa["h"]
		if (waW <= 0) || (waH <= 0)
			return 0
		hw := waW // 2
		hh := waH // 2
		switch snapType {
			case "left":
				return Map("x", waL, "y", waT, "w", hw, "h", waH)
			case "right":
				return Map("x", waL + (waW - hw), "y", waT, "w", hw, "h", waH)
			case "top":
				return Map("x", waL, "y", waT, "w", waW, "h", hh)
			case "bottom":
				return Map("x", waL, "y", waT + (waH - hh), "w", waW, "h", hh)
			case "topLeft":
				return Map("x", waL, "y", waT, "w", hw, "h", hh)
			case "topRight":
				return Map("x", waL + (waW - hw), "y", waT, "w", hw, "h", hh)
			case "bottomLeft":
				return Map("x", waL, "y", waT + (waH - hh), "w", hw, "h", hh)
			case "bottomRight":
				return Map("x", waL + (waW - hw), "y", waT + (waH - hh), "w", hw, "h", hh)
			default:
				return 0
		}
	}
}
