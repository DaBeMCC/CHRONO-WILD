> Historical report: E-based pickup was subsequently replaced by automatic contact collection. See `automatic-pickup.md` for the current mechanic and verification.

# Backpack corrections — 2026-11-13

Applied to `Assets/Prototype/DinoAI/DinoPrototype.unity` on Gerardo.

- Each of the 15 existing collectible instances has twice its previous local scale. The migration records completion per instance, so applying it again does not multiply scales again.
- Collectibles have a cyan point light (no shadows), plus the white proximity outline. Owned URP material copies replace incompatible Standard/Autodesk materials on those scene instances.
- Empty and occupied slots retain an independent four-edge border.
- P starts the black dimmer at alpha zero, with quadratic ease-in over 0.3 seconds. Content stays hidden for those 0.3 seconds, then fades in over 0.2 seconds. Closing or death cancels the reveal. UI uses unscaled time.
- Pickup uses a New Input System E action, a center-camera ray and obstruction checks. It ignores the player's own colliders. The inventory confirms the ID before the object is disabled and destroyed.
- Five generic prefab icons were replaced with rendered model previews.

## Verification

`BackpackVerification.Start()` runs inside Play Mode. It positions the test camera at each real collider, sends KeyboardState events through the New Input System, and checks removal and inventory updates. Dinosaur AI and player movement are disabled only for this test session; rigidbodies are made kinematic after entering Play Mode to keep aim deterministic. Stop Play Mode afterwards.

The attached text report records the final run: 15 objects collected in reverse ID order, fixed slot sprites, 15/15 cap, duplicate rejection, wall blocking E, P opening the panel, hidden content during the initial fade and completed reveal after 0.5 seconds. Empty and full backpack screenshots were inspected in the running Editor. Scripts compiled successfully.

This verifies the requested corrections. It is not a completion audit for every feature in the original implementation plan.
