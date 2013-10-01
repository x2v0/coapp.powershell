

$Source = @"
using System;
using System.Text;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace CoAppUtils {

//  public static class ModuleInitializer {
//        public static void Initialize() {
//            CoApp.Powershell.AssemblyResolver.Initialize();
//        }
//    }

    public static class PathUtil {
        public static string RelativePathTo(this string currentDirectory, string pathToMakeRelative) {
            if (string.IsNullOrEmpty(currentDirectory)) {
                throw new ArgumentNullException("currentDirectory");
            }

            if (string.IsNullOrEmpty(pathToMakeRelative)) {
                throw new ArgumentNullException("pathToMakeRelative");
            }

            currentDirectory = Path.GetFullPath(currentDirectory);
            pathToMakeRelative = Path.GetFullPath(pathToMakeRelative);

            if (!Path.GetPathRoot(currentDirectory).Equals(Path.GetPathRoot(pathToMakeRelative), StringComparison.CurrentCultureIgnoreCase)) {
                return pathToMakeRelative;
            }

            var relativePath = new List<string>();
            var currentDirectoryElements = currentDirectory.Split(Path.DirectorySeparatorChar);
            var pathToMakeRelativeElements = pathToMakeRelative.Split(Path.DirectorySeparatorChar);
            var commonDirectories = 0;

            for (; commonDirectories < Math.Min(currentDirectoryElements.Length, pathToMakeRelativeElements.Length); commonDirectories++) {
                if (
                    !currentDirectoryElements[commonDirectories].Equals(pathToMakeRelativeElements[commonDirectories], StringComparison.CurrentCultureIgnoreCase)) {
                    break;
                }
            }

            for (var index = commonDirectories; index < currentDirectoryElements.Length; index++) {
                if (currentDirectoryElements[index].Length > 0) {
                    relativePath.Add("..");
                }
            }

            for (var index = commonDirectories; index < pathToMakeRelativeElements.Length; index++) {
                relativePath.Add(pathToMakeRelativeElements[index]);
            }

            return string.Join(Path.DirectorySeparatorChar.ToString(), relativePath);
        }
    }
 }
"@

##  -ReferencedAssemblies CoApp.Powershell.Tools 

Add-Type -TypeDefinition $Source -Language CSharp  

## [CoAppUtils.ModuleInitializer]::Initialize( )


function Set-SignatureViaService
{
    param(
    [Parameter(Position = 0, Mandatory = $true, ValueFromPipeline= $true)]
    [ValidateNotNullOrEmpty()]
    [string[]] $FilePath,
    
    [Parameter(Position = 1)]
    [string] $OutputPath,

    [Parameter()]
    [switch] $StrongName,
    
    [Parameter()]
    [PSCredential]$Credential,

    [Parameter()]
    [string] $ServiceUrl
    )

    trap {
  Write-Error -Exception $_.Exception -Message @"
Error in script $($_.InvocationInfo.ScriptName) :
$($_.Exception) $($_.InvocationInfo.PositionMessage)
"@
 break;
    }



    #get azure-location
    $container = Get-UploadLocation -Remote -ServiceUrl $ServiceUrl -Credential $Credential
    
    $azureCred = Get-AzureCredentials -Remote -ServiceUrl $ServiceUrl -Credential $Credential -ContainerName $container[0]

    new-psdrive -name temp -psprovider azure -root $container[1] -credential $azureCred

    Copy-ItemEx $FilePath -Destination temp:


    pushd temp:
    
    # upload file
    
    
    $files = ls .
    
    
    foreach ($i in $files)
    {
    
        Set-CodeSignature $i -Remote -ServiceUrl $ServiceUrl -Credential $Credential
    }
    
     
    
    if (!$OutputPath)
    {
        $OutputPath = $FilePath
    }

    popd
    Write-Host $OutputPath
    #download file
    Copy-ItemEx temp: -Destination $OutputPath -Force -Verbose
    
    
    Remove-PSDrive temp
}

# New-Alias -Name ptk -Value Invoke-Build -Scope Global
# New-Alias -Name autopackage -Value Write-NuGetPackage -Scope Global
# New-Alias -Name apkg -Value Write-NuGetPackage -Scope Global

function Submit-Package {
  <#
  .SYNOPSIS
  Submits a NuGet package (or set of packages) to the gallery, but as hidden (unlisted).
  .DESCRIPTION
  Uploads the specified package (or all packages from a packages.config file) to the gallery and then immediately marks the package(s) as unlisted by running the nuget delete command.
  .EXAMPLE
  Submit-Package -packageId MyAwesomePackage -packageVersion 2.0.0.0 -packageFile MyAwesomePackage.2.0.0.nupkg -apiKey 00000000-0000-0000-0000-000000000000 -galleryUrl https://nuget.org
  .EXAMPLE
  Submit-Package -packagesConfig packages.config -apiKey 00000000-0000-0000-0000-000000000000 -galleryUrl https://nuget.org
  .PARAMETER packageId
  The Id of the package to hide/show
  .PARAMETER packageVersion
  The Version of the package to hide/show
  .PARAMETER packageFile
  The nupkg file to upload for the NuGet package
  .PARAMETER packagesConfig
  The XML config file that lists the packages to be hidden/shown
  .PARAMETER galleryUrl
  The NuGet gallery Url to connect to.  By default, https://nuget.org
  #>
  param(
    [Parameter(ParameterSetName="package")] $packageId,
    [Parameter(ParameterSetName="package")] $packageVersion,
    [Parameter(ParameterSetName="package")] $packageFile,
    [Parameter(ParameterSetName="config")]  $packagesConfig,
    $apiKey,
    $galleryUrl = "https://nuget.org"
  )

  If ($apiKey -eq $null) { throw "Parameter 'apiKey' was not specified" }
  If ($galleryUrl -eq $null) { throw "Parameter 'galleryUrl' was not specified" }

  If ($PSCmdlet.ParameterSetName -match "package") {
    If ($packageId -eq $null) { throw "Parameter 'packageId' was not specified" }
    If ($packageVersion -eq $null) { throw "Parameter 'packageVersion' was not specified" }
    If ($packageFile -eq $null) { throw "Parameter 'packageFile' was not specified" }

    $exists = Test-Path $packageFile
    if ($exists -eq $false)
    {
      throw "File not found: $packageFile"
    }

    PushDelete -packageId $packageId -packageVersion $packageVersion -packageFile $packageFile -apiKey $apiKey -galleryUrl $galleryUrl
  }
  ElseIf ($PSCmdlet.ParameterSetName -match "config") {
    If ($packagesConfig -eq $null) { throw "Parameter 'packagesConfig' was not specified" }
    If (!(Test-Path $packagesConfig)) { throw "File '$packagesConfig' was not found" }

    [xml]$packages = Get-Content $packagesConfig

    foreach ($package in $packages.packages.package) {
      $path = ".\" + $package.culture + "\" + $package.id + "." + $package.version + ".nupkg"
      $path = $path.Replace("\\", "\")

      $exists = Test-Path $path
      if ($exists -eq $false)
      {
        throw "File not found: $path"
      }

      PushDelete -packageId $package.id -packageVersion $package.version -packageFile $path -apiKey $apiKey -galleryUrl $galleryUrl
    }
  }
}

function Set-NuGetPackageVisibility {
  <#
  .SYNOPSIS
  Sets a package's visibility within the NuGet gallery
  .DESCRIPTION
  Hide (unlist) a package from the gallery or show (list) a package on the gallery.
  .EXAMPLE
  Set-PackageVisibility -action hide -packageId MyAwesomePackage -packageVersion 2.0.0.0 -apiKey 00000000-0000-0000-0000-000000000000 -galleryUrl https://nuget.org
  .EXAMPLE
  Set-PackageVisibility -action show -packageId MyAwesomePackage -packageVersion 2.0.0.0 -apiKey 00000000-0000-0000-0000-000000000000 -galleryUrl https://preview.nuget.org
  .PARAMETER action
  The action to take: hide or show
  .PARAMETER packageId
  The Id of the package to hide/show
  .PARAMETER packageVersion
  The Version of the package to hide/show
  .PARAMETER packagesConfig
  The XML config file that lists the packages to be hidden/shown
  .PARAMETER galleryUrl
  The NuGet gallery Url to connect to.  By default, https://nuget.org
  #>

  [CmdletBinding(DefaultParameterSetName='package')]
  param(
    $action,
    [Parameter(ParameterSetName="package")] $packageId,
    [Parameter(ParameterSetName="package")] $packageVersion,
    [Parameter(ParameterSetName="config")]  $packagesConfig,
    $apiKey,
    $galleryUrl = "https://nuget.org"
  )

  If ($action -eq $null) { throw "Parameter 'action' was not specified" }
  If ($apiKey -eq $null) { throw "Parameter 'apiKey' was not specified" }
  If ($galleryUrl -eq $null) { throw "Parameter 'galleryUrl' was not specified" }

  If ($PSCmdlet.ParameterSetName -match "package") {
    If ($packageId -eq $null) { throw "Parameter 'packageId' was not specified" }
    If ($packageVersion -eq $null) { throw "Parameter 'packageVersion' was not specified" }

    SetVisibility -action $action -packageId $packageId -packageVersion $packageVersion -apiKey $apiKey -galleryUrl $galleryUrl
  }
  ElseIf ($PSCmdlet.ParameterSetName -match "config") {
    If ($packagesConfig -eq $null) { throw "Parameter 'packagesConfig' was not specified" }
    If (!(Test-Path $packagesConfig)) { throw "File '$packagesConfig' was not found" }

    [xml]$packages = Get-Content $packagesConfig

    foreach ($package in $packages.packages.package) {
      SetVisibility -action $action -packageId $package.id -packageVersion $package.version -apiKey $apiKey -galleryUrl $galleryUrl
    }
  }
}

function Hide-NuGetPackage {
  <#
  .SYNOPSIS
  Hides a package from the NuGet gallery
  .DESCRIPTION
  Marks the specified NuGet package as unlisted, hiding it from the gallery.
  .EXAMPLE
  Hide-NuGetPackage -packageId MyAwesomePackage -packageVersion 2.0.0.0 -apiKey 00000000-0000-0000-0000-000000000000 -galleryUrl https://preview.nuget.org
  .PARAMETER packageId
  The Id of the package to hide
  .PARAMETER packageVersion
  The Version of the package to hide
  .PARAMETER packagesConfig
  The XML config file that lists the packages to be hidden/shown
  .PARAMETER galleryUrl
  The NuGet gallery Url to connect to.  By default, https://nuget.org
  #>

  param(
    [Parameter(ParameterSetName="package")] $packageId,
    [Parameter(ParameterSetName="package")] $packageVersion,
    [Parameter(ParameterSetName="config")]  $packagesConfig,
    $apiKey,
    $galleryUrl = "https://nuget.org"
  )

  If ($PSCmdlet.ParameterSetName -match "config") {
    Set-NuGetPackageVisibility -action hide -packagesConfig $packagesConfig -apiKey $apiKey -galleryUrl $galleryUrl
  }
  Else {
    Set-NuGetPackageVisibility -action hide -packageId $packageId -packageVersion $packageVersion -apiKey $apiKey -galleryUrl $galleryUrl
  }
}

function Show-NuGetPackage {
  <#
  .SYNOPSIS
  Shows a package on the NuGet gallery, listing an already-published but unlisted package.
  .DESCRIPTION
  Marks the specified NuGet package as listed, showing it on the gallery.
  .EXAMPLE
  Show-NuGetPackage -packageId MyAwesomePackage -packageVersion 2.0.0.0 -apiKey 00000000-0000-0000-0000-000000000000 -galleryUrl https://preview.nuget.org
  .PARAMETER packageId
  The Id of the package to show
  .PARAMETER packageVersion
  The Version of the package to show
  .PARAMETER packagesConfig
  The XML config file that lists the packages to be hidden/shown
  .PARAMETER galleryUrl
  The NuGet gallery Url to connect to.  By default, https://nuget.org
  #>

  param(
    [Parameter(ParameterSetName="package")] $packageId,
    [Parameter(ParameterSetName="package")] $packageVersion,
    [Parameter(ParameterSetName="config")]  $packagesConfig,
    $apiKey,
    $galleryUrl = "https://nuget.org"
  )

  If ($PSCmdlet.ParameterSetName -match "config") {
    Set-NuGetPackageVisibility -action show -packagesConfig $packagesConfig -apiKey $apiKey -galleryUrl $galleryUrl
  }
  Else {
    Set-NuGetPackageVisibility -action show -packageId $packageId -packageVersion $packageVersion -apiKey $apiKey -galleryUrl $galleryUrl
  }
}

function PushDelete {
  param(
    $packageId,
    $packageVersion,
    $packageFile,
    $apiKey,
    $galleryUrl
  )

  nuget push $packageFile -source $galleryUrl -apiKey $apiKey
  nuget delete $packageId $packageVersion -source $galleryUrl -noninteractive -apiKey $apiKey
}

function SetVisibility {
  param(
    $action,
    $packageId,
    $packageVersion,
    $apiKey,
    $galleryUrl
  )
  If ($action -match "hide") {
    $method = "DELETE"
    $message = "hidden (unlisted)"
  }
  ElseIf ($action -match "show") {
    $method = "POST"
    $message = "shown (listed)"
  }
  Else {
    throw "Invalid 'action' parameter value.  Valid values are 'hide' and 'show'."
  }

  $url = "$galleryUrl/api/v2/Package/$packageId/$packageVersion`?apiKey=$apiKey"
  $web = [System.Net.WebRequest]::Create($url)

  $web.Method = $method
  $web.ContentLength = 0

  Write-Host ""
  Write-Host "Submitting the $method request to $url..." -foregroundColor Cyan
  Write-Host ""

  $response = $web.GetResponse()

  If ($response.StatusCode -match "OK") {
    Write-Host "Package '$packageId' Version '$packageVersion' has been $message." -foregroundColor Green -backgroundColor Black
    Write-Host ""
  }
  Else {
    Write-Host $response.StatusCode
  }
}

function Show-CoAppEtcDirectory {
    explorer ((get-itemproperty  HKLM:\SOFTWARE\Outercurve\CoApp.Powershell\etc ).'(default)').Trim('/').Trim('\')
}

function Get-CoAppEtcDirectory {
    return ((get-itemproperty  HKLM:\SOFTWARE\Outercurve\CoApp.Powershell\etc ).'(default)').Trim('/').Trim('\')
}

function New-VCProject {
 param(
    [Parameter(Position = 0, Mandatory = $true, ValueFromPipeline= $true)]
    [ValidateNotNullOrEmpty()]
    [string] $OutputPath
    )
    
    $oldpwd = [System.Environment]::CurrentDirectory
    [System.Environment]::CurrentDirectory = $pwd

    $hasext = ($OutputPath.ToLower().EndsWith("vcxproj"))

    if( $hasext -eq $false ) {
       $OutputPath = $OutputPath + ".vcxproj"
    }

    $OutputPath = [System.IO.Path]::GetFullPath( $OutputPath )

    $exists = Test-Path $OutputPath
    if ($exists -eq $true)
    {
      throw "project file exists: $OutputPath"
    }

    $OutputDir = (split-path $OutputPath)
    
    $exists  = Test-Path $OutputDir 
    
    if ($exists -eq $false)
    {
      mkdir $OutputDir
    }

    $templ = Get-Content (((get-itemproperty  HKLM:\SOFTWARE\Outercurve\CoApp.Powershell\etc ).'(default)').Trim('/').Trim('\') + '\template-project.vcxproj' ) 
    set-content -Path $OutputPath -Value $templ.Replace("===NEWGUID===", ( [System.Guid]::NewGuid() ).ToString("B") )

    # copy-item (((get-itemproperty  HKLM:\SOFTWARE\Outercurve\CoApp.Powershell\etc ).'(default)').Trim('/').Trim('\') + '\template-project.vcxproj' ) $OutputPath
    [System.Environment]::CurrentDirectory = $oldpwd
}

function New-BuildInfo { 
    param(
        [Parameter(Position = 0, Mandatory = $true, ValueFromPipeline= $true)]
        [ValidateNotNullOrEmpty()]
        [string[]] $VCProjects
    )
    $oldpwd = [System.Environment]::CurrentDirectory
    [System.Environment]::CurrentDirectory = $pwd

    $exists = Test-Path ".buildinfo"
    if ($exists -eq $true)
    {
      throw ".buildinfo file exists"
    }

    $projs = ""
    
    foreach ($proj in $VCProjects) {
        $files = ls $proj
        foreach( $file in $files ) {
            $loc = [CoAppUtils.PathUtil]::RelativePathTo(  $PWD.Path , $file.FullName )
            $projs = $projs +"`r`n        "+$loc  + ","
        }
    }

    if( $projs -eq "" ) {
      throw "no projects found."
    }

    Write-Host Projects: $projs

    $templ = Get-Content (((get-itemproperty  HKLM:\SOFTWARE\Outercurve\CoApp.Powershell\etc ).'(default)').Trim('/').Trim('\') + '\buildinfo-template' ) 
    set-content -Path ".buildinfo" -Value $templ.Replace("===PROJECTS===",$projs).Trim(",")
    
    Write-Host Generated .buildinfo file.
    [System.Environment]::CurrentDirectory = $oldpwd
}

New-Alias -Name ptk -Value Invoke-Build 
New-Alias -Name autopackage -Value Write-NuGetPackage
New-Alias -Name apkg -Value Write-NuGetPackage 


#install-package name sourceurl version logpath force restart -whatif 
#

Export-ModuleMember -Function * -Alias * 
