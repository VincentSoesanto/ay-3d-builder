# Mixamo animations on your mech (Unity)

This makes a mech from **AY's 3D Builder** play **Humanoid animations**, for example walk, run or punch from Mixamo.
Its parts stay hard pieces that turn at the joints.

## What you need (once)
- Your Unity game project.
- **glTFast**, the free .glb loader:
  1. In Unity: **Window → Package Manager**.
  2. Click **+** (top left) → **Add package by name...**
  3. Type `com.unity.cloud.gltfast` → **Add**.
- **AYMechHumanoid.cs** (in this folder): copy it into your game project's **Assets** folder, for example by dragging it into Unity's Project window.

## 1. Make the mech in AY's 3D Builder
1. Build the mech, standing, facing **forward** (the way the Human reference faces).
2. Press **I**, then **K** (Bones panel) → **Skeleton: Biped**. Fit the joints with the handles.
3. Click **Auto-bind all**. Check with the **Pose test** that the parts follow the bones.
4. **Esc → Export .glb**. The file goes into the **Exports** folder next to the app.

## 2. Bring the mech into Unity
1. Drag the **.glb** into Unity's **Project** window (Assets).
2. Drag it from there into your **scene**.
3. Click it in the **Hierarchy** → **Inspector → Add Component** → type `AY Mech Humanoid` → add it.

## 3. Get animations from Mixamo
1. On **mixamo.com**, keep Mixamo's **own character** (you don't upload your mech).
2. Pick an animation, for example "Walking".
3. **Download** → Format **FBX for Unity (.fbx)**, Skin **Without Skin**, 30 fps → Download.
4. Drag the .fbx into Unity's Project window. Click it → **Inspector → Rig** tab → Animation Type **Humanoid** → **Apply**.
5. For walking or running: open the **Animation** tab of the same .fbx → tick **Loop Time** → **Apply**.

## 4. Put them together
1. Project window: **right click → Create → Animator Controller**. Name it, for example, `MechMoves`.
2. Double-click it. Drag the animation from your .fbx into the window. Click the small triangle on the .fbx to see the clip inside, often called "mixamo.com".
3. Click your mech → in **AY Mech Humanoid**, drag `MechMoves` into the **Controller** box.
4. Press **Play**. The mech moves.

## If something is wrong
- **Console says "Missing bones: ..."**: use the **Biped** skeleton. The names must stay Hips, Spine, Chest, Neck, Head, Shoulder / UpperArm / LowerArm / Hand .L .R, UpperLeg / LowerLeg / Foot .L .R.
- **Left and right look swapped**: the script finds the left side by position (facing forward, left = -X), so this should be rare. Make sure the mech faces forward (+Z).
- **A part doesn't move**: it isn't bound to a bone. Go back to the app and use **Auto-bind all** or **Bind selected pieces**.
- **Arms move strangely**: the script turns the arms straight out for Unity's T-pose. If your mech's arms already point out to the sides, untick **Arms Out For T Pose**.
