#Requires AutoHotkey v2.0

; ------------------------
; Minimal JSON (Parse/Stringify)
; ------------------------
class WcJsonParser {
	__New(text) {
		this.s := text
		this.i := 1
		this.len := StrLen(text)
	}

	Eof() => this.i > this.len

	Peek() {
		return this.Eof() ? "" : SubStr(this.s, this.i, 1)
	}

	Next() {
		ch := this.Peek()
		this.i += 1
		return ch
	}

	SkipWs() {
		while !this.Eof() {
			ch := this.Peek()
			if (ch = " ") || (ch = "`t") || (ch = "`r") || (ch = "`n")
				this.i += 1
			else
				break
		}
	}

	ParseValue() {
		this.SkipWs()
		ch := this.Peek()
		if (ch = "{")
			return this.ParseObject()
		if (ch = "[")
			return this.ParseArray()
		if (ch = '"')
			return this.ParseString()
		if (ch = "t")
			return this.ParseLiteral("true", true)
		if (ch = "f")
			return this.ParseLiteral("false", false)
		if (ch = "n")
			return this.ParseLiteral("null", "")
		return this.ParseNumber()
	}

	ParseLiteral(word, value) {
		if SubStr(this.s, this.i, StrLen(word)) != word
			throw Error("JSONリテラルが不正です")
		this.i += StrLen(word)
		return value
	}

	ParseObject() {
		obj := Map()
		this.Expect("{")
		this.SkipWs()
		if (this.Peek() = "}") {
			this.Next()
			return obj
		}
		loop {
			this.SkipWs()
			key := this.ParseString()
			this.SkipWs()
			this.Expect(":")
			val := this.ParseValue()
			obj[key] := val
			this.SkipWs()
			ch := this.Next()
			if (ch = "}")
				break
			if (ch != ",")
				throw Error("JSONオブジェクトの区切りが不正です")
		}
		return obj
	}

	ParseArray() {
		arr := []
		this.Expect("[")
		this.SkipWs()
		if (this.Peek() = "]") {
			this.Next()
			return arr
		}
		loop {
			val := this.ParseValue()
			arr.Push(val)
			this.SkipWs()
			ch := this.Next()
			if (ch = "]")
				break
			if (ch != ",")
				throw Error("JSON配列の区切りが不正です")
		}
		return arr
	}

	ParseString() {
		this.Expect('"')
		out := ""
		while !this.Eof() {
			ch := this.Next()
			if (ch = '"')
				return out
			if (ch = "\\") {
				esc := this.Next()
				switch esc {
					case '"': out .= '"'
					case "\\": out .= "\"
					case "/": out .= "/"
					case "b": out .= Chr(8)
					case "f": out .= Chr(12)
					case "n": out .= "`n"
					case "r": out .= "`r"
					case "t": out .= "`t"
					case "u":
						hex := SubStr(this.s, this.i, 4)
						if !RegExMatch(hex, "i)^[0-9a-f]{4}$")
							throw Error("\\uエスケープが不正です")
						this.i += 4
						out .= Chr("0x" hex)
					default:
						throw Error("文字列エスケープが不正です")
				}
			} else {
				out .= ch
			}
		}
		throw Error("JSON文字列が閉じていません")
	}

	ParseNumber() {
		this.SkipWs()
		start := this.i
		if (this.Peek() = "-")
			this.i += 1

		ch := this.Peek()
		if (ch = "0") {
			this.i += 1
		} else {
			if !RegExMatch(ch, "^[0-9]$")
				throw Error("JSON数値が不正です")
			while RegExMatch(this.Peek(), "^[0-9]$")
				this.i += 1
		}

		if (this.Peek() = ".") {
			this.i += 1
			if !RegExMatch(this.Peek(), "^[0-9]$")
				throw Error("JSON小数が不正です")
			while RegExMatch(this.Peek(), "^[0-9]$")
				this.i += 1
		}

		ch := this.Peek()
		if (ch = "e") || (ch = "E") {
			this.i += 1
			ch2 := this.Peek()
			if (ch2 = "+") || (ch2 = "-")
				this.i += 1
			if !RegExMatch(this.Peek(), "^[0-9]$")
				throw Error("JSON指数が不正です")
			while RegExMatch(this.Peek(), "^[0-9]$")
				this.i += 1
		}

		numStr := SubStr(this.s, start, this.i - start)
		if InStr(numStr, ".") || InStr(numStr, "e") || InStr(numStr, "E")
			return Number(numStr)
		try {
			return Integer(numStr)
		} catch {
			return Number(numStr)
		}
	}

	Expect(ch) {
		this.SkipWs()
		got := this.Next()
		if (got != ch)
			throw Error("JSON構文エラー: '" ch "' を期待しました")
	}
}

class WcJsonWriter {
	__New(pretty) {
		this.pretty := pretty
		this.sb := ""
		this.indent := 0
	}

	ToString() => this.sb

	WriteValue(val) {
		if (val is Map) {
			this.WriteObject(val)
			return
		}
		if (val is Array) {
			this.WriteArray(val)
			return
		}
		t := Type(val)
		if (t = "String") {
			this.sb .= this.EscapeString(val)
			return
		}
		if (t = "Integer") || (t = "Float") {
			this.sb .= val
			return
		}
		if (t = "Boolean") {
			this.sb .= (val ? "true" : "false")
			return
		}
		if (val = "") {
			this.sb .= "null"
			return
		}
		this.sb .= this.EscapeString(String(val))
	}

	WriteObject(map) {
		this.sb .= "{"
		keys := []
		for k, _ in map
			keys.Push(k)

		if (keys.Length = 0) {
			this.sb .= "}"
			return
		}

		this.indent += 1
		first := true
		for k in keys {
			if first {
				first := false
			} else {
				this.sb .= ","
			}
			this.NewLine()
			this.WriteIndent()
			this.sb .= this.EscapeString(String(k))
			this.sb .= this.pretty ? ": " : ":"
			this.WriteValue(map[k])
		}
		this.indent -= 1
		this.NewLine()
		this.WriteIndent()
		this.sb .= "}"
	}

	WriteArray(arr) {
		this.sb .= "["
		if (arr.Length = 0) {
			this.sb .= "]"
			return
		}
		this.indent += 1
		for idx, v in arr {
			if (idx > 1)
				this.sb .= ","
			this.NewLine()
			this.WriteIndent()
			this.WriteValue(v)
		}
		this.indent -= 1
		this.NewLine()
		this.WriteIndent()
		this.sb .= "]"
	}

	NewLine() {
		if this.pretty
			this.sb .= "`n"
	}

	WriteIndent() {
		if !this.pretty
			return
		Loop this.indent {
			this.sb .= "  "
		}
	}

	EscapeString(s) {
		; NOTE: AutoHotkey では '\\' はエスケープではないため、
		; Windowsパス等の単一バックスラッシュも明示的にJSON用へエスケープする。
		s := StrReplace(s, "\", "\\")
		s := StrReplace(s, '"', '\"')
		s := StrReplace(s, Chr(8), "\\b")
		s := StrReplace(s, Chr(12), "\\f")
		s := StrReplace(s, "`r", "\\r")
		s := StrReplace(s, "`n", "\\n")
		s := StrReplace(s, "`t", "\\t")
		return '"' s '"'
	}
}

class WcJson {
	static Parse(text) {
		p := WcJsonParser(text)
		val := p.ParseValue()
		p.SkipWs()
		if !p.Eof()
			throw Error("JSONの末尾に余分な文字があります")
		return val
	}

	static Stringify(val, pretty := false) {
		w := WcJsonWriter(pretty)
		w.WriteValue(val)
		return w.ToString()
	}
}
