using Godot;
using System;
using System.Collections.Generic;
using System.IO;

public partial class ControlHandSkeleton : Node3D
{
	[Export] public NodePath SkeletonPath;
	[Export] public bool EnableDataStream = false;
	[Export] public bool EnableMovementControl = true;  // Enable predefined movement playback
	[Export] public MovementMode Mode = MovementMode.AI;  // AI or Classifier mode
	[Export] public string ConfigFilePath = "user://movements.toml";  // Path to movement config file
	[Export] public float Frequency = 0.5f;  // Movement cycles per second
	[Export] public float HoldTime = 1.0f;  // Time to hold at max flexion (seconds)
	[Export] public float RestTime = 1.0f;  // Time to hold at rest (seconds)

	private Skeleton3D skeleton;
	private LSLCommunicationController communicationController;
	private List<float> currentData = [];

	// Bone name to index mapping
	private readonly Dictionary<string, int> boneMap = [];

	// Maximum movement for each joint (from Unity)
	private readonly float[][][] jointMaximumMovement = new float[16][][];

	// Movement control system
	private Dictionary<string, float[][][]> movementPoses;
	private string[] availableMovements;
	private int currentMovementIndex = 0;
	private string animationState = "waiting";  // waiting, closing, holding, opening, resting, frozen
	private float animationArgument = 0.0f;
	private float stateTimer = 0.0f;
	private DateTime animationStartTime;
	private FileSystemWatcher configWatcher;  // Watches for config file changes

	// Bone names in the FBX model (WaveBone naming convention)
	// Based on the Unity hand structure, mapping to WaveBone_0 through WaveBone_15
	private string[] boneNames =
	[
		"WaveBone_1",   // 0 - wrist
		"WaveBone_3",   // 1 - thumb2 (proximal)
		"WaveBone_4",   // 2 - thumb1 (middle)
		"WaveBone_5",   // 3 - thumb0 (distal)
		"WaveBone_7",   // 4 - index2 (proximal)
		"WaveBone_8",   // 5 - index1 (middle)
		"WaveBone_9",   // 6 - index0 (distal)
		"WaveBone_12",   // 7 - middle2 (proximal)
		"WaveBone_13",   // 8 - middle1 (middle)
		"WaveBone_14",   // 9 - middle0 (distal)
		"WaveBone_17",  // 10 - ring2 (proximal)
		"WaveBone_18",  // 11 - ring1 (middle)
		"WaveBone_19",  // 12 - ring0 (distal)
		"WaveBone_22",  // 13 - pinkie2 (proximal)
		"WaveBone_23",  // 14 - pinkie1 (middle)
		"WaveBone_24"   // 15 - pinkie0 (distal)
	];

	public override void _Ready()
	{
		GD.Print("=== Control Hand Skeleton Controller _Ready() START ===");

		// Get communication controller
		GD.Print("  Getting communication controller...");
		communicationController = GetNode<LSLCommunicationController>("/root/Main/LSLCommunicationController");
		GD.Print("  Communication controller found");

		// Find skeleton
		if (SkeletonPath != null)
		{
			skeleton = GetNode<Skeleton3D>(SkeletonPath);
		}
		else
		{
			// Try to find skeleton automatically
			skeleton = FindSkeletonRecursive(this);
		}

		if (skeleton != null)
		{
			GD.Print($"✅ Found Skeleton3D with {skeleton.GetBoneCount()} bones");
			MapBones();
		}
		else
		{
			GD.PrintErr("⚠️ No Skeleton3D found! Hand won't animate.");
		}

		// Set up maximum movements
		GD.Print("  Initializing maximum movements...");
		InitializeMaximumMovements();

		// Initialize movement control system
		if (EnableMovementControl)
		{
			GD.Print("  Initializing movement control system...");
			LoadMovementConfig();
			animationStartTime = DateTime.Now;

			if (movementPoses != null && availableMovements != null && availableMovements.Length > 0)
			{
				GD.Print($"  Movement mode: {Mode} ({availableMovements.Length} movements available)");
				GD.Print($"  Current movement: {availableMovements[currentMovementIndex]}");

				// Movement state will be sent when movement is first initiated (not while in waiting state)

				// Set up file watcher for hot-reload
				SetupConfigWatcher();
			}
			else
			{
				GD.PrintErr("  Failed to load movements - using empty movement list");
				availableMovements = [];
			}
		}

		// Apply skin color material to the hand mesh
		ApplySkinColor();

		GD.Print("=== Control Hand Skeleton Controller _Ready() COMPLETE ===");
	}

	private Skeleton3D FindSkeletonRecursive(Node node)
	{
		if (node is Skeleton3D skel)
			return skel;

		foreach (Node child in node.GetChildren())
		{
			var result = FindSkeletonRecursive(child);
			if (result != null)
				return result;
		}
		return null;
	}

	private void MapBones()
	{
		boneMap.Clear();

		// First, print all available bone names
		GD.Print($"\n  === Available bones in skeleton ({skeleton.GetBoneCount()} total) ===");
		for (int i = 0; i < skeleton.GetBoneCount(); i++)
		{
			GD.Print($"  [{i}] {skeleton.GetBoneName(i)}");
		}
		GD.Print("  ===============================================\n");

		GD.Print("\n  === Attempting to map hand joints to WaveBones ===");
		for (int i = 0; i < boneNames.Length; i++)
		{
			int boneIdx = skeleton.FindBone(boneNames[i]);
			if (boneIdx != -1)
			{
				boneMap[boneNames[i]] = boneIdx;
				GD.Print($"  Mapped {boneNames[i]} → bone index {boneIdx}");
			}
			else
			{
				GD.PrintErr($"  ⚠️ Bone '{boneNames[i]}' not found in skeleton!");
			}
		}
	}

	private void InitializeMaximumMovements()
	{
		for (int i = 0; i < jointMaximumMovement.Length; i++)
		{
			jointMaximumMovement[i] = new float[2][];
			for (int j = 0; j < 2; j++)
			{
				jointMaximumMovement[i][j] = new float[3];
			}
		}

		// Thumb (joints 1-3)
		jointMaximumMovement[1][0] = [-45, 0, 30];
		jointMaximumMovement[2][0] = [-55, 0, -35];
		jointMaximumMovement[3][0] = [-80, 0, 0];

		// Index (joints 4-6)
		jointMaximumMovement[4][0] = [-85, 0, 0];
		jointMaximumMovement[5][0] = [-75, 0, 0];
		jointMaximumMovement[6][0] = [-60, 0, 0];

		// Middle (joints 7-9)
		jointMaximumMovement[7][0] = [-85, 0, 0];
		jointMaximumMovement[8][0] = [-85, 0, 0];
		jointMaximumMovement[9][0] = [-60, 0, 0];

		// Ring (joints 10-12)
		jointMaximumMovement[10][0] = [-85, 0, 0];
		jointMaximumMovement[11][0] = [-85, 0, 0];
		jointMaximumMovement[12][0] = [-60, 0, 0];

		// Pinky (joints 13-15)
		jointMaximumMovement[13][0] = [-85, 0, 0];
		jointMaximumMovement[14][0] = [-85, 0, 0];
		jointMaximumMovement[15][0] = [-60, 0, 0];
	}

	public override void _Process(double delta)
	{
		// Handle movement control (keyboard input and animation)
		if (EnableMovementControl && !EnableDataStream)
		{
			HandleMovementInput();
			UpdateMovementAnimation((float)delta);
		}
		// Handle LSL data stream
		else if (EnableDataStream && communicationController != null)
		{
			currentData = communicationController.GetReceivedDataControl();

			if (currentData.Count >= 9 && skeleton != null)
			{
				MoveBonesFromStream();
			}
		}
	}

	public override void _PhysicsProcess(double delta)
	{
		SendControlHandData();
	}

	private void MoveBonesFromStream()
	{
		if (skeleton == null || boneMap.Count == 0)
			return;

		int shift = 0;

		// Thumb (uses indices 0 and 1: flexion and abduction)
		SetBoneRotation(1, currentData[0 + shift] * jointMaximumMovement[1][0][0], 0, currentData[1 + shift] * jointMaximumMovement[1][0][2]);
		SetBoneRotation(2, currentData[0 + shift] * jointMaximumMovement[2][0][0], 0, currentData[1 + shift] * jointMaximumMovement[2][0][2]);
		SetBoneRotation(3, currentData[0 + shift] * jointMaximumMovement[3][0][0], 0, currentData[1 + shift] * jointMaximumMovement[3][0][2]);

		// Index (uses index 2)
		SetBoneRotation(4, currentData[2 + shift] * jointMaximumMovement[4][0][0], 0, 0);
		SetBoneRotation(5, currentData[2 + shift] * jointMaximumMovement[5][0][0], 0, 0);
		SetBoneRotation(6, currentData[2 + shift] * jointMaximumMovement[6][0][0], 0, 0);

		// Middle (uses index 3)
		SetBoneRotation(7, currentData[3 + shift] * jointMaximumMovement[7][0][0], 0, 0);
		SetBoneRotation(8, currentData[3 + shift] * jointMaximumMovement[8][0][0], 0, 0);
		SetBoneRotation(9, currentData[3 + shift] * jointMaximumMovement[9][0][0], 0, 0);

		// Ring (uses index 4)
		SetBoneRotation(10, currentData[4 + shift] * jointMaximumMovement[10][0][0], 0, 0);
		SetBoneRotation(11, currentData[4 + shift] * jointMaximumMovement[11][0][0], 0, 0);
		SetBoneRotation(12, currentData[4 + shift] * jointMaximumMovement[12][0][0], 0, 0);

		// Pinky (uses index 5)
		SetBoneRotation(13, currentData[5 + shift] * jointMaximumMovement[13][0][0], 0, 0);
		SetBoneRotation(14, currentData[5 + shift] * jointMaximumMovement[14][0][0], 0, 0);
		SetBoneRotation(15, currentData[5 + shift] * jointMaximumMovement[15][0][0], 0, 0);
	}

	private void SetBoneRotation(int jointIndex, float xDeg, float yDeg, float zDeg)
	{
		if (jointIndex < 0 || jointIndex >= boneNames.Length)
			return;

		if (!boneMap.ContainsKey(boneNames[jointIndex]))
			return;

		int boneIdx = boneMap[boneNames[jointIndex]];

		// Create rotation from degrees (convert to radians)
		Vector3 eulerRadians = new(Mathf.DegToRad(xDeg), Mathf.DegToRad(yDeg), Mathf.DegToRad(zDeg));
		Quaternion rotation = new(Basis.FromEuler(eulerRadians));

		// Set the bone pose
		skeleton.SetBonePoseRotation(boneIdx, rotation);
	}

	private void SendControlHandData()
	{
		if (communicationController == null || skeleton == null || boneMap.Count == 0)
			return;

		List<float> outputData = [];

		// Extract current bone rotations and normalize them
		// Thumb Flexion
		var thumb2Rot = GetBoneRotationDegrees(1);
		outputData.Add(thumb2Rot.X / jointMaximumMovement[1][0][0]);

		// Thumb Abduction
		outputData.Add(thumb2Rot.Z / jointMaximumMovement[1][0][2]);

		// Index Flexion
		var index2Rot = GetBoneRotationDegrees(4);
		outputData.Add(index2Rot.X / jointMaximumMovement[4][0][0]);

		// Middle Flexion
		var middle2Rot = GetBoneRotationDegrees(7);
		outputData.Add(middle2Rot.X / jointMaximumMovement[7][0][0]);

		// Ring Flexion
		var ring2Rot = GetBoneRotationDegrees(10);
		outputData.Add(ring2Rot.X / jointMaximumMovement[10][0][0]);

		// Pinky Flexion
		var pinky2Rot = GetBoneRotationDegrees(13);
		outputData.Add(pinky2Rot.X / jointMaximumMovement[13][0][0]);

		// Wrist (not used, but needed for compatibility)
		outputData.Add(0); // Wrist Flexion
		outputData.Add(0); // Wrist Abduction
		outputData.Add(0); // Wrist Rotation

		communicationController.SendControlData(outputData);
	}

	private Vector3 GetBoneRotationDegrees(int jointIndex)
	{
		if (jointIndex < 0 || jointIndex >= boneNames.Length || !boneMap.ContainsKey(boneNames[jointIndex]))
			return Vector3.Zero;

		int boneIdx = boneMap[boneNames[jointIndex]];
		Quaternion rot = skeleton.GetBonePoseRotation(boneIdx);
		return rot.GetEuler() * (180.0f / Mathf.Pi);
	}

	public void ResetBones()
	{
		if (skeleton == null)
			return;

		foreach (var bone in boneMap.Values)
		{
			skeleton.SetBonePoseRotation(bone, Quaternion.Identity);
		}

		GD.Print("Control hand bones reset");
	}

	// ========== MOVEMENT CONTROL SYSTEM ==========

	private void HandleMovementInput()
	{
		// Left Arrow - Previous movement
		if (Input.IsActionJustPressed("ui_left"))
		{
			currentMovementIndex--;
			if (currentMovementIndex < 0)
				currentMovementIndex = availableMovements.Length - 1;

			ResetBones();
			animationState = "waiting";
			stateTimer = 0;
			GD.Print($"← Previous movement: {availableMovements[currentMovementIndex]}");
		}

		// Right Arrow - Next movement
		if (Input.IsActionJustPressed("ui_right"))
		{
			currentMovementIndex++;
			if (currentMovementIndex >= availableMovements.Length)
				currentMovementIndex = 0;

			ResetBones();
			animationState = "waiting";
			stateTimer = 0;
			GD.Print($"→ Next movement: {availableMovements[currentMovementIndex]}");
		}

		// Down Arrow - Start movement
		if (Input.IsActionJustPressed("ui_down"))
		{
			if (animationState == "waiting")
			{
				animationState = "closing";
				stateTimer = 0;
				animationStartTime = DateTime.Now;
				GD.Print($"▼ START movement: {availableMovements[currentMovementIndex]}");

				// Send current movement name via LSL when starting
				communicationController?.SendMovementState(availableMovements[currentMovementIndex].ToString());
			}
		}

		// Up Arrow - Stop movement
		if (Input.IsActionJustPressed("ui_up"))
		{
			animationState = "waiting";
			stateTimer = 0;
			ResetBones();
			GD.Print($"▲ STOP movement");

			// Send "Rest" via LSL when stopping
			communicationController?.SendMovementState("Rest");
		}

		// Space - Toggle freeze (hold at max flexion)
		if (Input.IsActionJustPressed("ui_accept"))
		{
			ToggleFreeze();
		}
	}

	/// <summary>
	/// Toggle freeze mode: holds the current movement at max flexion indefinitely.
	/// </summary>
	public void ToggleFreeze()
	{
		if (animationState == "frozen")
		{
			// Unfreeze - go back to waiting
			animationState = "waiting";
			stateTimer = 0;
			ResetBones();
			GD.Print($"▲ UNFREEZE movement");
			communicationController?.SendMovementState("Rest");
		}
		else
		{
			// Freeze - snap to max flexion and hold
			animationState = "frozen";
			animationArgument = Mathf.Pi * 0.5f;
			stateTimer = 0;
			GD.Print($"■ FREEZE movement: {availableMovements[currentMovementIndex]}");
			communicationController?.SendMovementState(availableMovements[currentMovementIndex].ToString());
		}
	}

	public bool IsFrozen => animationState == "frozen";

	private void UpdateMovementAnimation(float delta)
	{
		if (animationState == "waiting" || skeleton == null || movementPoses == null)
			return;

		// Frozen state: just hold at max flexion, no timer updates
		if (animationState == "frozen")
		{
			animationArgument = Mathf.Pi * 0.5f;
			// Fall through to apply poses below
		}

		string currentMovement = availableMovements[currentMovementIndex];
		if (!movementPoses.ContainsKey(currentMovement))
		{
			GD.PrintErr($"Movement '{currentMovement}' not found in poses dictionary");
			return;
		}

		float[][][] poses = movementPoses[currentMovement];

		stateTimer += delta;

		// State machine for movement animation
		switch (animationState)
		{
			case "closing":
				// Interpolate from rest (state 1) to max flexion (state 0)
				animationArgument = Mathf.Pi * 0.5f * (stateTimer / (1.0f / Frequency));
				if (animationArgument >= Mathf.Pi * 0.5f)
				{
					animationArgument = Mathf.Pi * 0.5f;
					animationState = "holding";
					stateTimer = 0;
				}
				break;

			case "holding":
				// Hold at max flexion
				animationArgument = Mathf.Pi * 0.5f;
				if (stateTimer >= HoldTime)
				{
					animationState = "opening";
					stateTimer = 0;
				}
				break;

			case "opening":
				// Interpolate from max flexion (state 0) to rest (state 1)
				animationArgument = Mathf.Pi * 0.5f * (1.0f - (stateTimer / (1.0f / Frequency)));
				if (animationArgument <= 0)
				{
					animationArgument = 0;
					animationState = "resting";
					stateTimer = 0;
				}
				break;

			case "resting":
				// Hold at rest
				animationArgument = 0;
				if (stateTimer >= RestTime)
				{
					animationState = "closing";
					stateTimer = 0;
				}
				break;
		}

		// Apply bone rotations with sine wave interpolation
		ApplyMovementPose(poses, animationArgument);
	}

	private void ApplyMovementPose(float[][][] poses, float argument)
	{
		if (skeleton == null || boneMap.Count == 0)
			return;

		float sinValue = -Mathf.Sin(argument);  // 0 to 1 interpolation

		for (int jointIdx = 0; jointIdx < 16; jointIdx++)
		{
			if (jointIdx >= boneNames.Length || !boneMap.ContainsKey(boneNames[jointIdx]))
				continue;

			// Get max flexion (state 0) and rest (state 1) poses
			float[] maxPose = poses[jointIdx][0];
			float[] restPose = poses[jointIdx][1];

			// Interpolate between rest and max using sine wave
			float x = restPose[0] + (maxPose[0] - restPose[0]) * sinValue;
			float y = restPose[1] + (maxPose[1] - restPose[1]) * sinValue;
			float z = restPose[2] + (maxPose[2] - restPose[2]) * sinValue;

			SetBoneRotation(jointIdx, x, y, z);
		}
	}

	public string GetCurrentMovementName()
	{
		if (!EnableMovementControl || availableMovements == null || currentMovementIndex >= availableMovements.Length)
			return "None";

		return availableMovements[currentMovementIndex];
	}

	public string GetAnimationState()
	{
		return animationState;
	}

	// ========== CONFIG LOADING SYSTEM ==========

	private void LoadMovementConfig()
	{
		string configPath = ProjectSettings.GlobalizePath(ConfigFilePath);

		// Generate default config if it doesn't exist
		if (!File.Exists(configPath))
		{
			GD.Print($"Config file not found at {configPath}, generating default...");
			MovementConfigGenerator.GenerateDefaultConfig(configPath);
		}

		// Load config
		var loadedPoses = MovementConfigLoader.LoadConfig(configPath);

		if (loadedPoses == null || loadedPoses.Count == 0)
		{
			GD.PrintErr("Failed to load movement config, falling back to hardcoded poses");
			// Fallback to hardcoded poses
			var hardcodedPoses = MovementPoses.GetMovementPoses();
			movementPoses = new Dictionary<string, float[][][]>();
			foreach (var kvp in hardcodedPoses)
			{
				movementPoses[kvp.Key.ToString()] = kvp.Value;
			}

			// Get movement list based on mode
			var hardcodedMovements = Mode == MovementMode.AI ?
				MovementPoses.AIModeMovements :
				MovementPoses.ClassifierModeMovements;
			availableMovements = new string[hardcodedMovements.Length];
			for (int i = 0; i < hardcodedMovements.Length; i++)
			{
				availableMovements[i] = hardcodedMovements[i].ToString();
			}
		}
		else
		{
			movementPoses = loadedPoses;
			availableMovements = MovementConfigLoader.GetMovementNames(loadedPoses);
			GD.Print($"Loaded {availableMovements.Length} movements from config");
		}
	}

	private void SetupConfigWatcher()
	{
		string configPath = ProjectSettings.GlobalizePath(ConfigFilePath);
		string directory = Path.GetDirectoryName(configPath);
		string filename = Path.GetFileName(configPath);

		if (!Directory.Exists(directory))
		{
			GD.PrintErr($"Config directory does not exist: {directory}");
			return;
		}

		try
		{
			configWatcher = new FileSystemWatcher(directory, filename);
			configWatcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size;
			configWatcher.Changed += OnConfigFileChanged;
			configWatcher.EnableRaisingEvents = true;
			GD.Print($"Watching config file: {configPath}");
		}
		catch (Exception e)
		{
			GD.PrintErr($"Failed to set up config watcher: {e.Message}");
		}
	}

	private DateTime lastConfigReload = DateTime.MinValue;
	private void OnConfigFileChanged(object sender, FileSystemEventArgs e)
	{
		// Debounce: ignore rapid consecutive changes
		if ((DateTime.Now - lastConfigReload).TotalMilliseconds < 500)
			return;

		lastConfigReload = DateTime.Now;

		GD.Print($"Config file changed, reloading...");

		// Call deferred to avoid threading issues
		CallDeferred(nameof(ReloadConfig));
	}

	private void ReloadConfig()
	{
		string configPath = ProjectSettings.GlobalizePath(ConfigFilePath);
		var loadedPoses = MovementConfigLoader.LoadConfig(configPath);

		if (loadedPoses != null && loadedPoses.Count > 0)
		{
			int oldIndex = currentMovementIndex;
			string oldMovement = availableMovements[oldIndex];

			movementPoses = loadedPoses;
			availableMovements = MovementConfigLoader.GetMovementNames(loadedPoses);

			// Try to preserve current movement selection
			currentMovementIndex = Array.IndexOf(availableMovements, oldMovement);
			if (currentMovementIndex < 0)
				currentMovementIndex = 0;

			GD.Print($"Config reloaded: {availableMovements.Length} movements");

			// Reset animation state to prevent issues
			animationState = "waiting";
			ResetBones();
		}
		else
		{
			GD.PrintErr("Config reload failed, keeping current configuration");
		}
	}

	/// <summary>
	/// Load a new config file, replacing the current one.
	/// Updates ConfigFilePath, reloads movements, and re-watches the new file.
	/// </summary>
	public void LoadConfigFile(string absolutePath)
	{
		var loadedPoses = MovementConfigLoader.LoadConfig(absolutePath);
		if (loadedPoses == null || loadedPoses.Count == 0)
		{
			GD.PrintErr($"Failed to load config from: {absolutePath}");
			return;
		}

		// Convert to user:// path if inside user data dir, otherwise use globalized path
		string userDir = ProjectSettings.GlobalizePath("user://");
		if (absolutePath.StartsWith(userDir))
			ConfigFilePath = "user://" + absolutePath.Substring(userDir.Length);
		else
			ConfigFilePath = absolutePath;

		// Apply loaded movements
		movementPoses = loadedPoses;
		availableMovements = MovementConfigLoader.GetMovementNames(loadedPoses);
		currentMovementIndex = 0;
		animationState = "waiting";
		ResetBones();

		GD.Print($"Loaded config: {absolutePath} ({availableMovements.Length} movements)");

		// Re-watch the new file
		if (configWatcher != null)
		{
			configWatcher.EnableRaisingEvents = false;
			configWatcher.Dispose();
			configWatcher = null;
		}
		SetupConfigWatcher();
	}

	public override void _ExitTree()
	{
		// Clean up file watcher
		if (configWatcher != null)
		{
			configWatcher.EnableRaisingEvents = false;
			configWatcher.Dispose();
			configWatcher = null;
		}
	}

	private void ApplySkinColor()
	{
		// Find all MeshInstance3D children recursively and apply skin color
		var meshInstances = FindMeshInstancesRecursive(this);

		if (meshInstances.Count == 0)
		{
			GD.Print("  No mesh instances found for skin color application");
			return;
		}

		// Create a skin-colored material (peachy/beige skin tone)
		var skinMaterial = new StandardMaterial3D();
		skinMaterial.AlbedoColor = new Color(0.95f, 0.76f, 0.65f); // Light peachy skin tone
		skinMaterial.Roughness = 0.7f;
		skinMaterial.Metallic = 0.0f;

		// Apply material to all mesh instances
		foreach (var meshInstance in meshInstances)
		{
			// Override surface material
			for (int i = 0; i < meshInstance.GetSurfaceOverrideMaterialCount(); i++)
			{
				meshInstance.SetSurfaceOverrideMaterial(i, skinMaterial);
			}
			// If no override materials, set the material directly
			if (meshInstance.GetSurfaceOverrideMaterialCount() == 0 && meshInstance.Mesh != null)
			{
				for (int i = 0; i < meshInstance.Mesh.GetSurfaceCount(); i++)
				{
					meshInstance.SetSurfaceOverrideMaterial(i, skinMaterial);
				}
			}
		}

		GD.Print($"  Applied skin color material to {meshInstances.Count} mesh instance(s)");
	}

	private List<MeshInstance3D> FindMeshInstancesRecursive(Node node)
	{
		var meshes = new List<MeshInstance3D>();

		if (node is MeshInstance3D meshInstance)
		{
			meshes.Add(meshInstance);
		}

		foreach (Node child in node.GetChildren())
		{
			meshes.AddRange(FindMeshInstancesRecursive(child));
		}

		return meshes;
	}
}
