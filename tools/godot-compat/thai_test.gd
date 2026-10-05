extends SceneTree

func _initialize():
	var zw = String.chr(0x200B)
	var p1 = ["นักเดินทาง", "จาก", "แดน", "ไกล"]
	var p2 = ["เจ้า", "มาถึง", "หมู่บ้าน", "แห่ง", "นี้", "เพื่อ", "เหตุใด"]
	var plain = "".join(p1) + " " + "".join(p2)
	var zwsp = zw.join(p1) + " " + zw.join(p2)
	print("[sanity] plain == zwsp-with-zwsp-stripped: ", plain == zwsp.replace(zw, ""))

	var ts = TextServerManager.get_primary_interface()
	print("[server] primary interface: ", ts.get_interface_name() if ts.has_method("get_interface_name") else "?")

	var font = SystemFont.new()
	font.font_names = PackedStringArray(["Thonburi", "Sarabun", "Noto Sans Thai"])
	var w_test = font.get_string_size("ทดสอบ", HORIZONTAL_ALIGNMENT_LEFT, -1, 20)
	print("[sanity] width('ทดสอบ') @20px = ", w_test.x)
	if w_test.x <= 0.0:
		push_error("font not loaded, aborting wrap test")
		quit(1)
		return

	var width = 260.0
	var lh = font.get_height(20)
	for flags_name in ["default", "BREAK_WORD_BOUND", "BREAK_ADAPTIVE"]:
		var flags = 0
		if flags_name == "default":
			flags = TextServer.BREAK_MANDATORY | TextServer.BREAK_WORD_BOUND
		elif flags_name == "BREAK_WORD_BOUND":
			flags = TextServer.BREAK_WORD_BOUND
		else:
			flags = TextServer.BREAK_MANDATORY | TextServer.BREAK_WORD_BOUND | TextServer.BREAK_ADAPTIVE
		var sz_plain = font.get_multiline_string_size(plain, HORIZONTAL_ALIGNMENT_LEFT, width, 20, -1, flags)
		var sz_zwsp = font.get_multiline_string_size(zwsp, HORIZONTAL_ALIGNMENT_LEFT, width, 20, -1, flags)
		print("[wrap ", flags_name, "] width=260 plain_lines=", int(round(sz_plain.y / lh)),
			" zwsp_lines=", int(round(sz_zwsp / lh if false else sz_zwsp.y / lh)))

	# กว้างขึ้น 420px เพื่อดูว่าจำนวนบรรทัดลดตามคาด
	var sz2 = font.get_multiline_string_size(zwsp, HORIZONTAL_ALIGNMENT_LEFT, 420.0, 20)
	print("[wrap width=420 zwsp] lines=", int(round(sz2.y / lh)))
	quit(0)
