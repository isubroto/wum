import re, sys, io

src = open(".github/workflows/release.yml", encoding="utf-8").read().splitlines()

# Find the "Create WinGet Manifests" step's run: | block, capture until dedent drops.
out = []
i = 0
in_run = False
base = None
# locate step
for idx, line in enumerate(src):
    if "name: Create WinGet Manifests" in line:
        start = idx
        break
# find 'run: |' after start
j = start
while "run: |" not in src[j]:
    j += 1
run_key_indent = len(src[j]) - len(src[j].lstrip())
# content lines: indent > run_key_indent, until a line with indent <= run_key_indent (non-blank)
content = []
k = j + 1
for line in src[k:]:
    if line.strip() == "":
        content.append("")
        continue
    indent = len(line) - len(line.lstrip())
    if indent <= run_key_indent:
        break
    content.append(line)

# strip common leading indent (GitHub block-scalar dedent) = min indent of non-blank lines
indents = [len(l) - len(l.lstrip()) for l in content if l.strip()]
strip = min(indents)
ded = []
for l in content:
    if l.strip() == "":
        ded.append("")
    else:
        ded.append(l[strip:])
script = "\n".join(ded)

# Inject mock values for GitHub expressions used in this block.
repl = {
    "'${{ steps.versions.outputs.safe }}'": "'0.3.0.58'",
    "${{ github.repository }}": "SubrotoSaha/wum",
}
for a, b in repl.items():
    script = script.replace(a, b)

# The block reads a real MSI; replace MSI-dependent parts with mocks so we can run offline.
# Cut everything from MSI checks up to the ProductCode resolution, substitute mocks.
script = re.sub(
    r"\$msiName = .*?Write-Host \"Final ProductCode: '\$cleanProductCode'\"",
    "\n".join([
        "$msiName = \"wum-$version.msi\"",
        "$msiUrl = \"$repoUrl/releases/download/v$version/$msiName\"",
        "$hash = 'ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789'",
        "$cleanProductCode = '{12345678-1234-1234-1234-123456789012}'",
    ]),
    script, count=1, flags=re.S)

# Redirect winget output dir to winget-test to avoid clobber.
script = script.replace('"winget/', '"winget-test/').replace("-Path \"winget\"", "-Path \"winget-test\"").replace('-Path "winget"', '-Path "winget-test"')

open(".github/workflows/_gen.ps1", "w", encoding="utf-8").write(script)
print("written _gen.ps1, lines:", len(script.splitlines()))
