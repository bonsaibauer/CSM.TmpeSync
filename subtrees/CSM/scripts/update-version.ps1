# Sourced from: https://github.com/sebastianlux/tfbuildtask-updateAssemblyInfo/blob/master/UpdateAssemblyInfoTask/UpdateAssemblyInfo.ps1
# Used in the orginal Azure Pipelines task

Param (
    [string]$rootFolder = $(get-location).Path,
    [string]$filePattern,
    [string]$assemblyProduct,
    [string]$assemblyDescription,
    [string]$assemblyCopyright,
    [string]$assemblyCompany,
    [string]$assemblyTrademark,
    [string]$assemblyConfiguration,
    [string]$assemblyVersion, 
    [string]$assemblyFileVersion, 
    [string]$assemblyInformationalVersion,
    [string]$customAttributes
)

Write-Host ([Environment]::NewLine)
Write-Host ("Parameters:")
Write-Host ("----------")

# Write all params to the console.
Write-Host ("Root folder: " + $rootFolder)
Write-Host ("File pattern: " + $filePattern)
Write-Host ("Product: " + $assemblyProduct)
Write-Host ("Description: " + $assemblyDescription)
Write-Host ("Copyright: " + $assemblyCopyright)
Write-Host ("Company: " + $assemblyCompany)
Write-Host ("Trademark: " + $assemblyTrademark)
Write-Host ("Configuration: " + $assemblyConfiguration)
Write-Host ("Version: " + $assemblyVersion)
Write-Host ("File version: " + $assemblyFileVersion)
Write-Host ("InformationalVersion: " + $assemblyInformationalVersion)
Write-Host ("Custom attributes: " + $customAttributes)

function UpdateAssemblyInfo()
{
    Write-Host ([Environment]::NewLine)
    Write-Host ("Searching for files...")
    Write-Host("============================================================")

    foreach ($file in $input) 
    {
        
        Write-Host ($file.FullName)

        $tmpFile = $file.FullName + ".tmp"

        $fileContent = Get-Content $file.FullName -encoding utf8

        Write-Host ([Environment]::NewLine)
        Write-Host("Updating attributes")
        Write-Host("-------------------")
        $fileContent = TryReplace "AssemblyProduct" $assemblyProduct;
        $fileContent = TryReplace "AssemblyDescription" $assemblyDescription;
        $fileContent = TryReplace "AssemblyCopyright" $assemblyCopyright;
        $fileContent = TryReplace "AssemblyCompany" $assemblyCompany;
        $fileContent = TryReplace "AssemblyTrademark" $assemblyTrademark;
        $fileContent = TryReplace "AssemblyConfiguration" $assemblyConfiguration;
        $fileContent = TryReplace "AssemblyVersion" $assemblyVersion;
        $fileContent = TryReplace "AssemblyFileVersion" $assemblyFileVersion;
        $fileContent = TryReplace "AssemblyInformationalVersion" $assemblyInformationalVersion;
        
        if($customAttributes)
        {
            Write-Host ([Environment]::NewLine)
            Write-Host("Updating custom attributes")
            Write-Host("--------------------------")
            $fileContent = WriteCustomAttributes $customAttributes;
        }

        Write-Host ([Environment]::NewLine)
        Write-Host("Saving changes...")

        Set-Content $tmpFile -value $fileContent -encoding utf8
    
        Move-Item $tmpFile $file.FullName -force

        Write-Host ([Environment]::NewLine)
        Write-Host("============================================================")
        Write-Host ([Environment]::NewLine)
    }

    Write-Host("Done!")
}

function TryReplace($attributeKey, $value)
{
    if($value)
    {
        $containsAttributeKey = $fileContent | %{$_ -match $attributeKey}

        If($containsAttributeKey -contains $true)
        {
            Write-Host("Updating '$attributeKey'...")

            if($file.Extension -eq ".vb")
            {
                $attribute = $attributeKey + '("' + $value + '")';
            }
            else
            {
                $attribute = $attributeKey + '(@"' + $value + '")';
            }

            $fileContent = $fileContent -replace ($attributeKey +'\(@{0,1}".*"\)'), $attribute
        }
        else
        {
            Write-Host("Skipped '$attributeKey' (not found in file)")
        }
    }
    else
    {
        Write-Host("Skipped '$attributeKey' (no value defined)")
    }

    return $fileContent
}

function NormalizeVersionString([string]$versionString)
{
    if([string]::IsNullOrWhiteSpace($versionString))
    {
        return $null
    }

    $normalized = $versionString.Trim()
    if($normalized.Length -gt 0 -and ($normalized[0] -eq 'v' -or $normalized[0] -eq 'V'))
    {
        $normalized = $normalized.Substring(1)
    }

    $versionStringRegex = [System.Text.RegularExpressions.Regex]::Match($normalized, "^[0-9]+(\.[0-9]+){1,3}$");
    if($versionStringRegex.Success)
    {
        return $normalized
    }

    return $null
}

function ValidateVersionString($versionString, [ref]$normalized)
{
    $normalized.Value = $null

    if([string]::IsNullOrWhiteSpace($versionString))
    {
        return $true
    }

    $result = NormalizeVersionString $versionString
    if($null -eq $result)
    {
        return $false
    }

    $normalized.Value = $result
    return $true
}

function ValidateParams()
{
    $normalized = $null

    if(-not (ValidateVersionString $assemblyVersion ([ref]$normalized)))
    {
        Write-Host ("'$assemblyVersion' is not a valid parameter for attribute 'AssemblyVersion'")
        return $false
    }
    if($assemblyVersion)
    {
        $script:assemblyVersion = $normalized.Value
    }

    $normalized = $null
    if(-not (ValidateVersionString $assemblyFileVersion ([ref]$normalized)))
    {
        Write-Host ("'$assemblyFileVersion' is not a valid parameter for attribute 'AssemblyFileVersion'")
        return $false
    }
    if($assemblyFileVersion)
    {
        $script:assemblyFileVersion = $normalized.Value
    }

    if($assemblyInformationalVersion)
    {
        $normalizedInfo = NormalizeVersionString $assemblyInformationalVersion
        if($normalizedInfo)
        {
            $script:assemblyInformationalVersion = $normalizedInfo
        }
    }

    return $true
}

function WriteCustomAttributes($customAttributes)
{
    foreach($customAttribute in ($customAttributes -split ';'))
    {
        $customAttributeKey, $customAttributeValue = $customAttribute.Split('=')
      
        $fileContent = TryReplace $customAttributeKey $customAttributeValue
    }

    return $fileContent
}

if(ValidateParams)
{
    Get-Childitem -Path $rootFolder -recurse |? {$_.Name -like $filePattern} | UpdateAssemblyInfo; 
}
