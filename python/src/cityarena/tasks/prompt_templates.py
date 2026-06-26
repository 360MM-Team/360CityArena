LOCALIZATION_STEP_LIMIT = 50

LOCALIZATION_PROMPT = f"""Your task is to explore the city and determine your initial starting position.
A reference map with a grid overlay is provided in the Task Reference Images. You MUST pick your answer from this grid: select the single grid cell that corresponds to your starting location and output its grid coordinate.

Before answering, actively explore your surroundings to gain confidence in your estimate: move around (walk a short distance), rotate your view (left/right and up/down), and re-check landmarks from multiple angles. Do not provide your answer until you are confident in your location.

You must complete your exploration and give your final answer within {LOCALIZATION_STEP_LIMIT} steps.

When you have determined your starting position, specify "ANSWER" in the "action" field of your JSON response and provide your answer in the "answer" field in the format: "x:[grid_x] y:[grid_y]", where [grid_x] and [grid_y] are the integer indices of the selected grid cell from the provided grid. Do not output continuous coordinates (e.g., meters); only output the discrete grid indices."""

LOCALIZATION_IMAGES = ["benchmark/assets/localization/map_localization.png"]

MAP_NAVIGATION_PROMPT = """Your task is to navigate from the starting position to the goal destination using the available actions.

A navigation reference map is provided in the task description above. On this reference map:
- BLUE marker indicates your starting position (initial location)
- RED marker indicates your goal destination

When you reach the goal area, use the ANSWER action to confirm completion."""

LANDMARK_SEARCH_WITH_LANGUAGE_PROMPT = """Your task is to go to {LandmarkName}. When you get in front of {LandmarkName}, use the ANSWER action to confirm completion.

The goal is not far from the starting point.
"""

LANDMARK_SEARCH_WITH_IMAGE_PROMPT = """Your task is to go to the landmark shown in the task reference image. When you get in front of the landmark, use the ANSWER action to confirm completion.

The goal is not far from the starting point.
"""

LANGUAGE_GUIDED_NAVIGATION_PROMPT = """Your task is to follow the directions to reach your destination. Please follow the instructions below:

{Directions}

Once you have reached your destination, output the ANSWER action."""

RELATIONAL_SPATIAL_REASONING_PROMPT = """Find a nearby {LandmarkName} and tell me the name of {Relation}. {LandmarkName} is right nearby."""

COUNTING_PROMPT = """Your task is to count the number of {Object} within {Range}.

Only count items that belong to the specified area/side/segment relative to your current position and along the described road segment. Do not include items outside the specified range.

If the specified range describes a block, count along the full perimeter of that block (all four sides) unless a specific side is explicitly specified (e.g., "right side only").

Output the answer as a number.
"""
