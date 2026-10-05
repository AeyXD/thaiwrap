extends SceneTree

func _initialize():
	var zw = String.chr(0x200B)
	# วลีเดียวไม่มี space เลย — ถ้าไม่มีจุดตัดจะล้นหรือถูกตัดกลางคำ
	var words = ["นักเดินทาง", "จากแดนไกล", "มาถึงหมู่บ้าน", "แห่งนี้", "เพื่อ", "เหตุใด"]
	var plain = "".join(words)
	var zwsp = zw.join(words)

	var font = SystemFont.new()
	font.font_names = PackedStringArray(["Thonburi", "Sarabun", "Noto Sans Thai"])
	var lh = font.get_height(20)
	var width = 200.0
	var szp = font.get_multiline_string_size(plain, HORIZONTAL_ALIGNMENT_LEFT, width, 20, -1, TextServer.BREAK_MANDATORY | TextServer.BREAK_WORD_BOUND)
	var szz = font.get_multiline_string_size(zwsp, HORIZONTAL_ALIGNMENT_LEFT, width, 20, -1, TextServer.BREAK_MANDATORY | TextServer.BREAK_WORD_BOUND)
	print("[nospace w=200] plain: lines=", int(round(szp.y / lh)), " bbox_x=", szp.x)
	print("[nospace w=200] zwsp : lines=", int(round(szz.y / lh)), " bbox_x=", szz.x)

	# ปิด word-bound เหลือ grapheme-bound: Godot จะยอมตัดทุก grapheme (เหมือน issue #99474)
	var szg = font.get_multiline_string_size(plain, HORIZONTAL_ALIGNMENT_LEFT, width, 20, -1, TextServer.BREAK_MANDATORY | TextServer.BREAK_WORD_BOUND | TextServer.BREAK_GRAPHEME_BOUND)
	print("[nospace w=200] plain+GRAPHEME_BOUND: lines=", int(round(szg.y / lh)), " bbox_x=", szg.x)
	quit(0)
