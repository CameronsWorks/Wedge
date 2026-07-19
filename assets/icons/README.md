Grid-icon overrides.

The client renders each item's grid icon from its bundle. A couple of the ported
items render wrong at icon size, so the client substitutes a pre-made image
instead. Drop a PNG here named for the item and it gets embedded into the client
build:

- m8230.png - Model 8230 CS gas grenade (dark upright canister), ~63px

The shipped m8230 icon is rendered from the item's own model, so it matches what
you see in the 3D inspect view. Replace the file and rebuild to change it.
