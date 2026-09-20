# Shared guard for recursive operations on generated repository output.
function Assert-GeneratedPath {
    param(
        [Parameter(Mandatory = $true)][string]$RepositoryRoot,
        [Parameter(Mandatory = $true)][string]$Path
    )

    $root = [IO.Path]::GetFullPath($RepositoryRoot).TrimEnd([IO.Path]::DirectorySeparatorChar)
    $rootPrefix = $root + [IO.Path]::DirectorySeparatorChar
    $target = [IO.Path]::GetFullPath($Path)
    if (-not $target.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing generated-output operation outside the repository: $target"
    }
    $relative = $target.Substring($rootPrefix.Length).Replace('\', '/')
    $tracked = & git --literal-pathspecs -C $root ls-files -- $relative
    if ($LASTEXITCODE -ne 0) { throw "Cannot check tracked files in $root." }
    if ($tracked) { throw "Refusing generated-output operation on tracked contents: $target" }

    # Check ancestors first, then walk one level at a time without following links.
    $ancestor = $target
    while ($ancestor -and $ancestor.Length -ge $root.Length) {
        if (Test-Path -LiteralPath $ancestor) {
            $item = Get-Item -LiteralPath $ancestor -Force
            if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) {
                throw "Refusing generated-output operation through a reparse point: $ancestor"
            }
        }
        $ancestor = Split-Path -Parent $ancestor
    }
    if (-not (Test-Path -LiteralPath $target -PathType Container)) { return }
    $pending = [Collections.Generic.Stack[string]]::new()
    $pending.Push($target)
    while ($pending.Count -gt 0) {
        foreach ($item in Get-ChildItem -LiteralPath $pending.Pop() -Force) {
            if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) {
                throw "Refusing generated-output operation containing a reparse point: $($item.FullName)"
            }
            if ($item.Name -eq '.git') { throw "Refusing generated-output operation containing a repository: $($item.FullName)" }
            if ($item.PSIsContainer) { $pending.Push($item.FullName) }
        }
    }
}
