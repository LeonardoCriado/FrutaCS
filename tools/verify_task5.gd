# Task 5 headless verification: empties a full AK mag into the thin wall from
# spawn (comparing the impact pattern against the Task 4 recoil-table dump),
# then drives movement (run / walk / bhop feel numbers).
# Order matters: the pattern test fires from pristine spawn; movement tests
# run after (they shove the player against the wall, past its edge).
# Run: godot --headless --script res://tools/verify_task5.gd
extends SceneTree

var _failures: Array[String] = []


func _init() -> void:
	_run()


func _run() -> void:
	var packed: PackedScene = load("res://scenes/TestRange.tscn")
	if packed == null:
		_fail("scene load", "TestRange.tscn did not load")
		_finish()
		return
	var scene: Node = packed.instantiate()
	root.add_child(scene)
	await physics_frame
	await physics_frame
	var player: Node = scene.get_node("PlayerBody")
	var weapon: Node = player.get_node("Head/Camera3D/WeaponView")
	release_all()
	await await_n_frames(30)

	# 1. Crosshair honesty: standing spread is the AK table value verbatim.
	check_range("stand spread", weapon.get("CurrentSpreadDeg"), 0.3150, 0.3152)

	# 2. Empty a full mag into the thin wall from spawn (480 u, centered).
	Input.action_press("fire")
	var guard := 0
	while int(weapon.call("ImpactCount")) < 30 and guard < 600:
		await physics_frame
		guard += 1
	Input.action_release("fire")
	check_true("30 impacts logged", int(weapon.call("ImpactCount")) == 30)
	check_true("mag empty", int(weapon.get("MagAmmo")) == 0)

	var ys: Array = []
	var xs: Array = []
	var bad_dmg := 0
	for i in range(30):
		var p: Vector3 = weapon.call("GetImpactPos", i)
		ys.append(p.y)
		xs.append(p.x)
		# Thin wall (40 u) penetrated, thick wall behind stops: 2 stages,
		# 36 * 0.98^0.96 * 0.6^2 = 12. Impact marks the first surface.
		if int(weapon.call("GetImpactDamage", i)) != 12:
			bad_dmg += 1
		if absf(p.z - (-480.0)) > 4.0:
			bad_dmg += 1
	check_true("all impacts on thin-wall face dealing staged damage 12", bad_dmg == 0)
	# Table: pitch 0.55 -> 4.0 over shots 1-6, 0.55 -> 9.3 over 1-30, at 480 u.
	check_range("first impact height", ys[0], 60.0, 78.0)
	check_range("shots 1-6 climb", float(ys[5]) - float(ys[0]), 18.0, 40.0)
	check_range("full-mag climb", float(ys.max()) - float(ys.min()), 55.0, 95.0)
	# Table yaw swings -1.95 .. +1.80 -> ~31 u lateral at 480 u.
	check_range("lateral spread", float(xs.max()) - float(xs.min()), 18.0, 48.0)

	# 3. View punch recovers.
	await await_n_frames(60)
	var punch: Vector2 = weapon.get("ViewPunchDeg")
	check_true("punch recovered", punch.length() < 0.5)

	# 4. Run reaches ~250 u/s.
	release_all()
	Input.action_press("move_forward")
	await await_n_frames(120)
	check_range("run speed", player.get("HorizontalSpeedU"), 245.0, 255.0)

	# 5. Walk (Shift) wires WalkSpeed ~130 u/s.
	Input.action_press("walk")
	await await_n_frames(120)
	check_range("walk speed", player.get("HorizontalSpeedU"), 120.0, 140.0)
	release_all()

	# 6. Bhop: rebuild speed, jump, strafe perpendicular while airborne.
	Input.action_press("move_forward")
	await await_n_frames(60)
	Input.action_release("move_forward")
	Input.action_press("jump")
	await await_n_frames(1)
	Input.action_release("jump")
	await await_n_frames(2)
	check_true("airborne after jump", not bool(player.get("SimOnFloor")))
	Input.action_press("move_right")
	await await_n_frames(30)
	check_true("bhop gains speed", float(player.get("HorizontalSpeedU")) > 300.0)

	_finish()


func await_n_frames(n: int) -> void:
	for i in range(n):
		await physics_frame


func release_all() -> void:
	for a in ["move_forward", "move_back", "move_left", "move_right", "jump", "walk", "duck", "fire", "weapon_secondary", "reload"]:
		Input.action_release(a)


func check_range(label: String, value: float, lo: float, hi: float) -> void:
	if value >= lo and value <= hi:
		print("PASS ", label, " = ", value)
	else:
		_fail(label, str(value) + " not in [" + str(lo) + ", " + str(hi) + "]")


func check_true(label: String, cond: bool) -> void:
	if cond:
		print("PASS ", label)
	else:
		_fail(label, "condition false")


func _fail(label: String, detail: String) -> void:
	_failures.append(label + ": " + detail)
	print("FAIL ", label, " — ", detail)


func _finish() -> void:
	release_all()
	if _failures.is_empty():
		print("VERIFY_TASK5: ALL PASS")
		quit(0)
	else:
		print("VERIFY_TASK5: ", _failures.size(), " FAILURES")
		quit(1)
