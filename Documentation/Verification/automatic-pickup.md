# Automatic collection

This replaces the previous E-based pickup mechanic. Applied to DinoPrototype.

- All 15 objects are centered at world Y=0.9, half their previous Y=1.8. The migration is idempotent.
- Kinematic rigidbodies with gravity disabled keep them hovering. They bob ±0.08 metres at 0.35 Hz and rotate at 18 degrees/second.
- The aura's point-light intensity smoothly cycles between 0 and 2.5 over 3 seconds, with a different phase per object.
- A 0.65 metre trigger collects on player contact. OnTriggerStay supports contacts continuing after pause. Walls, pause, death and duplicate IDs still prevent collection. Objects disappear only after the inventory accepts their ID.
- Each item has two survival or portal-related uses. Four randomly chosen phrase templates avoid immediate repetition. Notifications queue, appear at the bottom for 5 seconds and fade in/out.

Verification: `BackpackVerification.Start()` in Play Mode disables dinosaur AI and normal player input for the test, then moves the real CharacterController through all 15 trigger zones without keyboard input. It verifies float and aura changes, collection only after contact, inventory counts and fixed slot icons. The final report is `automatic-pickup.txt`. The bottom notification was visually inspected in the running Editor. After stopping Play Mode, saved scene heights, kinematic bodies and trigger configuration were checked for all collectibles. Scripts compiled successfully.
