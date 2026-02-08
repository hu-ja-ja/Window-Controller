#Requires AutoHotkey v2.0

global WC_GUI_IsReloadingProfiles := false
global WC_GUI_IsSorting := false
global WC_GUI_PendingSortWins := 0
global WC_GUI_PendingSortProfiles := 0
global WC_GUI_SortTimerArmedWins := false
global WC_GUI_SortTimerArmedProfiles := false

OpenWindowControllerGui(wc, openProfilesFirst := false) {
	static wcGui := 0
	static lvWins := 0
	static lvProfiles := 0
	static edtName := 0
	static chkSync := 0
	static txtStatus := 0
	static btnApply := 0
	static btnLaunchApply := 0
	static btnDelete := 0

	if wcGui {
		try wcGui.Show()
		return
	}

	wcGui := Gui(, "Window-Controller")
	wcGui.OnEvent("Close", (*) => wcGui.Hide())

	wcGui.SetFont("s10")
	colCheckW := Max(22, Round(22 * (A_ScreenDPI / 96)))

	wcGui.AddText("xm ym", "起動中ウィンドウ（チェックしてプロファイル保存）")
	lvWins := wcGui.AddListView("xm w920 r18 Checked", ["", "Title", "Exe", "Class", "HWND", "URL"])
	lvWins.ModifyCol(1, colCheckW)
	lvWins.ModifyCol(2, 320)
	lvWins.ModifyCol(3, 120)
	lvWins.ModifyCol(4, 140)
	lvWins.ModifyCol(5, 80)
	lvWins.ModifyCol(6, 200)
	; 1列目ヘッダクリックでチェック状態ソート
	lvWins.OnEvent("ColClick", (ctrl, col) => _OnWinsColClick(ctrl, col))

	btnRefresh := wcGui.AddButton("xm y+8 w120", "更新")
	btnRefresh.OnEvent("Click", (*) => _RefreshWindowList(wc, lvWins, txtStatus))

	wcGui.AddText("x+12 yp+6", "プロファイル名:")
	edtName := wcGui.AddEdit("x+6 yp-3 w260", "")
	btnSave := wcGui.AddButton("x+10 yp w160", "チェックを保存")
	btnSave.OnEvent("Click", (*) => _SaveProfileFromChecked(wc, lvWins, edtName, lvProfiles, txtStatus))

	wcGui.AddText("xm y+14", "保存済みプロファイル（チェック=最小化/最大化を常時連動）")
	lvProfiles := wcGui.AddListView("xm w920 r8 Checked", ["", "Profile", "Windows"])
	lvProfiles.ModifyCol(1, colCheckW)
	lvProfiles.ModifyCol(2, 460)
	lvProfiles.ModifyCol(3, 80)
	; ItemCheck は (Ctrl, Row, Checked?) のように情報が渡ってくるため、それを受け取る
	lvProfiles.OnEvent("ItemCheck", (ctrl, row, checked := "") => _OnProfileItemCheck(wc, ctrl, txtStatus, row, checked))
	; 1列目ヘッダクリックでチェック状態ソート
	lvProfiles.OnEvent("ColClick", (ctrl, col) => _OnProfilesColClick(ctrl, col))

	btnApply := wcGui.AddButton("xm y+8 w180", "適用（配置のみ）")
	btnApply.OnEvent("Click", (*) => _ApplySelectedProfile(wc, lvProfiles, false, txtStatus))

	btnLaunchApply := wcGui.AddButton("x+10 yp w220", "一括起動＋配置")
	btnLaunchApply.OnEvent("Click", (*) => _ApplySelectedProfile(wc, lvProfiles, true, txtStatus))

	btnDelete := wcGui.AddButton("x+10 yp w140", "削除")
	btnDelete.OnEvent("Click", (*) => _DeleteSelectedProfile(wc, lvProfiles, txtStatus))

	chkSync := wcGui.AddCheckBox("xm y+10", "連動機能を有効にする（全体）")
	chkSync.Value := wc.Config["settings"]["syncMinMax"] ? 1 : 0
	chkSync.OnEvent("Click", (*) => _ToggleSync(wc, chkSync, txtStatus))

	txtStatus := wcGui.AddText("xm y+10 w920", "")

	_ReloadProfiles(wc, lvProfiles)
	_RefreshWindowList(wc, lvWins, txtStatus)

	if openProfilesFirst {
		if (lvProfiles.GetCount() = 0) {
			txtStatus.Text := "プロファイルがありません。まずチェックして保存してください。"
		}
	}

	wcGui.Show()
}

_RefreshWindowList(wc, lv, txtStatus) {
	try {
		lv.Delete()
		wins := wc.EnumerateWindows()
		for w in wins {
			lv.Add(, "", w["title"], w["exe"], w["class"], w["hwnd"], w["url"])
		}
		txtStatus.Text := "ウィンドウ一覧を更新しました（" wins.Length "件）"
	} catch as ex {
		wc.Log("GUI refresh failed: " ex.Message)
		txtStatus.Text := "更新に失敗しました: " ex.Message
	}
}

_GetCheckedHwnds(lv) {
	hwnds := []
	row := 0
	while (row := lv.GetNext(row, "Checked")) {
		hwndText := lv.GetText(row, 5)
		if (hwndText != "") {
			try hwnds.Push(Integer(hwndText))
		}
	}
	return hwnds
}

_SaveProfileFromChecked(wc, lv, edtName, lvProfiles, txtStatus) {
	name := Trim(edtName.Value)
	hwnds := _GetCheckedHwnds(lv)
	if (hwnds.Length = 0) {
		txtStatus.Text := "チェックされたウィンドウがありません。"
		return
	}
	try {
		wc.CaptureProfileFromHwnds(name, hwnds)
		_ReloadProfiles(wc, lvProfiles, name)
		txtStatus.Text := "保存しました: " name
	} catch as ex {
		wc.Log("Save profile failed: " ex.Message)
		MsgBox("保存に失敗しました。`n" ex.Message, "Window-Controller", 0x10)
		txtStatus.Text := "保存に失敗: " ex.Message
	}
}

_ReloadProfiles(wc, lvProfiles, selectName := "") {
	global WC_GUI_IsReloadingProfiles
	WC_GUI_IsReloadingProfiles := true
	try {
		lvProfiles.Delete()
		items := wc.GetProfilesForGui()
		for it in items {
			row := lvProfiles.Add(, "", it["name"], it["count"])
			if it["syncMinMax"]
				lvProfiles.Modify(row, "Check")
			if (selectName != "") && (it["name"] = selectName)
				lvProfiles.Modify(row, "Select Focus Vis")
		}
		if (selectName = "") && (lvProfiles.GetCount() > 0)
			lvProfiles.Modify(1, "Select Focus")
	} finally {
		WC_GUI_IsReloadingProfiles := false
	}
}


_GetSelectedProfileName(lvProfiles) {
	row := lvProfiles.GetNext(0, "Focused")
	if !row
		row := lvProfiles.GetNext(0)
	if !row
		return ""
	return lvProfiles.GetText(row, 2)
}

_ApplySelectedProfile(wc, lvProfiles, launchMissing, txtStatus) {
	name := _GetSelectedProfileName(lvProfiles)
	if (Trim(name) = "") {
		txtStatus.Text := "プロファイルを選択してください。"
		return
	}
	try {
		result := wc.ApplyProfile(name, launchMissing)
		msg := name " を適用: " result["appliedCount"] "/" result["totalCount"]
		if (result["failures"].Length > 0) {
			msg .= "（失敗 " result["failures"].Length "件）"
			details := ""
			for f in result["failures"]
				details .= "- " f "`n"
			MsgBox("一部失敗しました:`n`n" details, "Window-Controller", 0x30)
		}
		txtStatus.Text := msg
	} catch as ex {
		wc.Log("Apply profile failed: " ex.Message)
		MsgBox("適用に失敗しました。`n" ex.Message, "Window-Controller", 0x10)
		txtStatus.Text := "適用に失敗: " ex.Message
	}
}

_DeleteSelectedProfile(wc, lvProfiles, txtStatus) {
	name := _GetSelectedProfileName(lvProfiles)
	if (Trim(name) = "") {
		txtStatus.Text := "プロファイルを選択してください。"
		return
	}
	if (MsgBox("削除しますか？: " name, "Window-Controller", 0x24) != "Yes")
		return
	try {
		ok := wc.DeleteProfile(name)
		if ok {
			; 削除後は先頭を選択
			_ReloadProfiles(wc, lvProfiles)
			txtStatus.Text := "削除しました: " name
		} else {
			txtStatus.Text := "削除できませんでした: " name
		}
	} catch as ex {
		wc.Log("Delete profile failed: " ex.Message)
		txtStatus.Text := "削除に失敗: " ex.Message
	}
}


_OnProfileItemCheck(wc, lvProfiles, txtStatus, row := 0, checked := "") {
	global WC_GUI_IsReloadingProfiles
	if WC_GUI_IsReloadingProfiles
		return
	if (!row) {
		; フォールバック（環境によっては A_EventInfo が使える）
		row := A_EventInfo
	}
	if !row
		return
	name := lvProfiles.GetText(row, 2)
	if (Trim(name) = "")
		return
	; ItemCheckはチェック状態が反映される前に呼ばれることがあるため、1tick遅延して読む
	SetTimer((*) => _ApplyProfileCheckState(wc, lvProfiles, row, name, txtStatus, checked), -1)
}

_ApplyProfileCheckState(wc, lvProfiles, row, name, txtStatus, checked := "") {
	global WC_GUI_IsReloadingProfiles
	if WC_GUI_IsReloadingProfiles
		return
	if (checked != "") {
		isChecked := (checked ? true : false)
	} else {
		isChecked := (lvProfiles.GetNext(row - 1, "Checked") = row)
	}
	try {
		wc.SetProfileSyncMinMax(name, isChecked)
		txtStatus.Text := "連動設定(" name "): " (isChecked ? "ON" : "OFF")
	} catch as ex {
		wc.Log("SetProfileSyncMinMax failed: " ex.Message)
		txtStatus.Text := "連動設定に失敗: " ex.Message
	}
}

_OnProfilesColClick(lvProfiles, col) {
	global WC_GUI_IsReloadingProfiles
	global WC_GUI_IsSorting
	global WC_GUI_PendingSortProfiles
	global WC_GUI_SortTimerArmedProfiles
	static lastCol := 0
	static asc := true
	if (lastCol = col)
		asc := !asc
	else
		asc := true
	lastCol := col
	; NOTE: 速いクリックでも取りこぼさないよう、要求を保留してタイマーでまとめて処理する
	WC_GUI_PendingSortProfiles := Map(
		"lv", lvProfiles,
		"columnCount", 3,
		"keyCol", 2,
		"sortCol", col,
		"asc", asc,
		"suppressProfileEvents", true
	)
	if !WC_GUI_SortTimerArmedProfiles {
		WC_GUI_SortTimerArmedProfiles := true
		SetTimer(_ProcessPendingSortProfiles, -1)
	}
}

_OnWinsColClick(lvWins, col) {
	global WC_GUI_IsSorting
	global WC_GUI_PendingSortWins
	global WC_GUI_SortTimerArmedWins
	static lastCol := 0
	static asc := true
	if (lastCol = col)
		asc := !asc
	else
		asc := true
	lastCol := col
	; NOTE: 速いクリックでも取りこぼさないよう、要求を保留してタイマーでまとめて処理する
	WC_GUI_PendingSortWins := Map(
		"lv", lvWins,
		"columnCount", 6,
		"keyCol", 5,
		"sortCol", col,
		"asc", asc,
		"suppressProfileEvents", false
	)
	if !WC_GUI_SortTimerArmedWins {
		WC_GUI_SortTimerArmedWins := true
		SetTimer(_ProcessPendingSortWins, -1)
	}
}

_SortListViewByColumn_Safe(lv, columnCount, keyCol, sortCol, asc, suppressProfileEvents := false) {
	global WC_GUI_IsReloadingProfiles
	global WC_GUI_IsSorting
	if WC_GUI_IsSorting
		return
	WC_GUI_IsSorting := true
	try {
		if suppressProfileEvents {
			WC_GUI_IsReloadingProfiles := true
			try {
				_SortListViewByColumn(lv, columnCount, keyCol, sortCol, asc)
			} finally {
				WC_GUI_IsReloadingProfiles := false
			}
		} else {
			_SortListViewByColumn(lv, columnCount, keyCol, sortCol, asc)
		}
	} finally {
		WC_GUI_IsSorting := false
	}
}

_ProcessPendingSortWins() {
	global WC_GUI_PendingSortWins
	global WC_GUI_SortTimerArmedWins
	global WC_GUI_IsSorting
	WC_GUI_SortTimerArmedWins := false
	if !WC_GUI_PendingSortWins
		return
	if WC_GUI_IsSorting {
		; ソート中に要求が来た場合は、終わってからもう一度
		WC_GUI_SortTimerArmedWins := true
		SetTimer(_ProcessPendingSortWins, -1)
		return
	}
	req := WC_GUI_PendingSortWins
	WC_GUI_PendingSortWins := 0
	_SortListViewByColumn_Safe(req["lv"], req["columnCount"], req["keyCol"], req["sortCol"], req["asc"], req["suppressProfileEvents"])
	; 実行中に新しい要求が入っていたら、もう一度回す
	if WC_GUI_PendingSortWins {
		WC_GUI_SortTimerArmedWins := true
		SetTimer(_ProcessPendingSortWins, -1)
	}
}

_ProcessPendingSortProfiles() {
	global WC_GUI_PendingSortProfiles
	global WC_GUI_SortTimerArmedProfiles
	global WC_GUI_IsSorting
	WC_GUI_SortTimerArmedProfiles := false
	if !WC_GUI_PendingSortProfiles
		return
	if WC_GUI_IsSorting {
		WC_GUI_SortTimerArmedProfiles := true
		SetTimer(_ProcessPendingSortProfiles, -1)
		return
	}
	req := WC_GUI_PendingSortProfiles
	WC_GUI_PendingSortProfiles := 0
	_SortListViewByColumn_Safe(req["lv"], req["columnCount"], req["keyCol"], req["sortCol"], req["asc"], req["suppressProfileEvents"])
	if WC_GUI_PendingSortProfiles {
		WC_GUI_SortTimerArmedProfiles := true
		SetTimer(_ProcessPendingSortProfiles, -1)
	}
}

_SortListViewByColumn(lv, columnCount, keyCol, sortCol, asc := true) {
	selectedRow := lv.GetNext(0, "Focused")
	if !selectedRow
		selectedRow := lv.GetNext(0)
	selectedKey := ""
	if selectedRow
		try selectedKey := lv.GetText(selectedRow, keyCol)

	items := []
	Loop lv.GetCount() {
		r := A_Index
		data := []
		Loop columnCount {
			data.Push(lv.GetText(r, A_Index))
		}
		isChecked := (lv.GetNext(r - 1, "Checked") = r)
		items.Push(Map(
			"data", data,
			"checked", isChecked,
			"idx", r
		))
	}

	cmp := (a, b) => _SortListViewByColumn_Compare(a, b, sortCol, asc)
	_StableInsertionSort(items, cmp)

	lv.Delete()
	for it in items {
		newRow := lv.Add(, it["data"]*)
		if it["checked"]
			lv.Modify(newRow, "Check")
	}

	if (selectedKey != "") {
		Loop lv.GetCount() {
			r := A_Index
			if (lv.GetText(r, keyCol) = selectedKey) {
				lv.Modify(r, "Select Focus Vis")
				break
			}
		}
	}
}

_SortListViewByColumn_Compare(a, b, sortCol, asc) {
	; 戻り値: -1/0/1
	if (sortCol = 1) {
		av := a["checked"] ? 1 : 0
		bv := b["checked"] ? 1 : 0
		cmp := (av < bv) ? -1 : (av > bv ? 1 : 0)
	} else {
		at := a["data"][sortCol]
		bt := b["data"][sortCol]
		; 数値っぽい列は数値比較（HWNDなど）
		if RegExMatch(at, "^-?\\d+$") && RegExMatch(bt, "^-?\\d+$") {
			ai := Integer(at)
			bi := Integer(bt)
			cmp := (ai < bi) ? -1 : (ai > bi ? 1 : 0)
		} else {
			cmp := StrCompare(String(at), String(bt), false)
		}
	}

	if (cmp = 0) {
		; 安定化のため元の並びをタイブレークにする
		cmp := (a["idx"] < b["idx"]) ? -1 : (a["idx"] > b["idx"] ? 1 : 0)
	}
	return asc ? cmp : -cmp
}

_StableInsertionSort(arr, cmpFn) {
	len := arr.Length
	if (len <= 1)
		return
	Loop len - 1 {
		i := A_Index + 1
		key := arr[i]
		j := i - 1
		while (j >= 1) {
			if (cmpFn(key, arr[j]) < 0) {
				arr[j + 1] := arr[j]
				j -= 1
				continue
			}
			break
		}
		arr[j + 1] := key
	}
}


_SortListViewByCheckedState(lv, columnCount, keyCol, asc := true) {
	; チェック状態(true/false)で手動ソートする（ネイティブチェックボックスの見た目は維持）
	selectedRow := lv.GetNext(0, "Focused")
	if !selectedRow
		selectedRow := lv.GetNext(0)
	selectedKey := ""
	if selectedRow
		try selectedKey := lv.GetText(selectedRow, keyCol)

	unchecked := []
	checked := []
	Loop lv.GetCount() {
		r := A_Index
		data := []
		Loop columnCount {
			data.Push(lv.GetText(r, A_Index))
		}
		isChecked := (lv.GetNext(r - 1, "Checked") = r)
		item := Map("checked", isChecked, "data", data)
		if isChecked
			checked.Push(item)
		else
			unchecked.Push(item)
	}

	lv.Delete()
	if asc {
		_SortListViewByCheckedState_AddGroup(lv, unchecked)
		_SortListViewByCheckedState_AddGroup(lv, checked)
	} else {
		_SortListViewByCheckedState_AddGroup(lv, checked)
		_SortListViewByCheckedState_AddGroup(lv, unchecked)
	}

	if (selectedKey != "") {
		Loop lv.GetCount() {
			r := A_Index
			if (lv.GetText(r, keyCol) = selectedKey) {
				lv.Modify(r, "Select Focus Vis")
				break
			}
		}
	}
}

_SortListViewByCheckedState_AddGroup(lv, group) {
	for it in group {
		newRow := lv.Add(, it["data"]*)
		if it["checked"]
			lv.Modify(newRow, "Check")
	}
}

_ToggleSync(wc, chkSync, txtStatus) {
	try {
		wc.SetSyncMinMax(chkSync.Value = 1)
		txtStatus.Text := "連動設定: " (chkSync.Value = 1 ? "ON" : "OFF")
	} catch as ex {
		wc.Log("Toggle sync failed: " ex.Message)
		txtStatus.Text := "設定変更に失敗: " ex.Message
	}
}

