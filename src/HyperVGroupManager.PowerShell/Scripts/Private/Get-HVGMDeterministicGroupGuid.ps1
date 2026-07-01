function Get-HVGMDeterministicGroupGuid {
    param(
        [Parameter(Mandatory)]
        [string]$GroupName
    )
    # First 16 bytes of SHA-256 over a namespaced string become a stable GUID.
    # Used when Hyper-V does not assign a GUID to a VM group (observed on Windows Server 2025).
    # The same name always produces the same GUID, giving C# a stable identifier across reloads.
    $bytes = [System.Text.Encoding]::UTF8.GetBytes("HyperVGroupManager:VMGroup:$GroupName")
    $hash  = [System.Security.Cryptography.SHA256]::Create().ComputeHash($bytes)
    return [System.Guid]::new($hash[0..15])
}
