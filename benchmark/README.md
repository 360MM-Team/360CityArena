# Benchmark Data

This directory contains the public 360CityArena task data and task reference assets.

- `tasks/`: CSV task definitions. Each file represents one public task family.
- `assets/localization/`: localization reference map.
- `assets/landmark_images/`: landmark-search image references.
- `assets/navigation_maps/`: map-navigation references.
- `manifests/`: release inventory metadata.

Task loaders fail closed: missing CSVs, invalid rows, duplicate task IDs, and missing required fields raise explicit errors instead of silently dropping tasks.
