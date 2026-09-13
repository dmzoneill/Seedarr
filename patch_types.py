import sys

with open('src/Seedarr.Frontend/src/api/types.ts', 'r') as f:
    content = f.read()

new_props = """  shouldPause: boolean;
  shouldResume: boolean;
  shouldRemove: boolean;
  deleteDataOnRemove: boolean;
  shouldRecheck: boolean;
  shouldReannounce: boolean;
  shouldBoostTracker: boolean;"""

content = content.replace("  shouldPause: boolean;\n  shouldResume: boolean;\n  shouldRemove: boolean;\n  deleteDataOnRemove: boolean;", new_props)

with open('src/Seedarr.Frontend/src/api/types.ts', 'w') as f:
    f.write(content)
