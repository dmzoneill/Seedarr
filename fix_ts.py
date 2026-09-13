import sys

with open('src/Seedarr.Frontend/src/pages/AutomationPage.tsx', 'r') as f:
    content = f.read()

# Fix torrent undefined
content = content.replace('placeholder={`{"event": "complete", "torrent": "${torrent.name}", "size": ${torrent.size}}`}', 'placeholder={`{"event": "complete", "torrent": "\\${torrent.name}", "size": \\${torrent.size}}`}')

# Fix step.http.body and register
content = content.replace('step.http.body.trim()', 'step.http.body?.trim()')
content = content.replace('JSON.parse(step.http.body)', 'JSON.parse(step.http.body!)')
content = content.replace('step.http.body.replace', 'step.http.body!.replace')
content = content.replace('step.http.register.trim()', 'step.http.register?.trim()')
content = content.replace('step.http.register.trim()', 'step.http.register!.trim()')

# Fix setTestResult
content = content.replace('''          setTestResult({
            success: false,
            error: "Cannot run script: missing ID",
            executionTimeMs: 0,
            tagsToAdd: [],
            tagsToRemove: [],
            shouldPause: false,
            shouldResume: false,
            shouldRemove: false,
            deleteDataOnRemove: false,
          });''', '''          setTestResult({
            success: false,
            error: "Cannot run script: missing ID",
            executionTimeMs: 0,
            tagsToAdd: [],
            tagsToRemove: [],
            shouldPause: false,
            shouldResume: false,
            shouldRemove: false,
            deleteDataOnRemove: false,
            shouldRecheck: false,
            shouldReannounce: false,
            shouldBoostTracker: false,
          });''')

with open('src/Seedarr.Frontend/src/pages/AutomationPage.tsx', 'w') as f:
    f.write(content)
