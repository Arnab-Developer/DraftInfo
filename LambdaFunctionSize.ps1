using namespace System.Collections.Generic

param(
    [Parameter(Mandatory=$true)]
    [ValidateNotNullOrEmpty()]
    [string]
    $Environment,

    [bool] $ShowLayers = $false
)

class FunctionInfo
{
    [string] $Name
    [long] $CodeSize
    [List[LayerInfo]] $Layers

    FunctionInfo([string] $name, [long] $codeSize)
    {
        $this.Name = $name
        $this.CodeSize = $codeSize
        $this.Layers = [List[LayerInfo]]::new()
    }
}

class LayerInfo
{
    [string] $Name
    [long] $CodeSize

    LayerInfo([string] $name, [long] $codeSize)
    {
        $this.Name = $name
        $this.CodeSize = $codeSize
    }
}

function GetFunctionInfos([string[]] $functionNames) 
{
    $functionInfos = [List[FunctionInfo]]::new()

    foreach ($functionName in $functionNames)
    {
        $output = aws lambda get-function `
            --function-name $functionName `
            --query 'Configuration.{CodeSize: CodeSize, Layers: Layers}' `
            --output json | ConvertFrom-Json

        $functionInfo = [FunctionInfo]::new($functionName, $output.CodeSize)

        foreach ($layer in $output.Layers)
        {
            $layerInfo = [LayerInfo]::new(($layer.Arn -split ':')[6], $layer.CodeSize)
            $functionInfo.Layers.Add($layerInfo)
        }

        $functionInfos.Add($functionInfo)    
        Write-Debug "Lambda function read complete: $($functionInfo.Name)"
    }

    return $functionInfos
}

function DisplayFunctionInfos([List[FunctionInfo]] $functionInfos) 
{
    $sortedFunctionInfos = $functionInfos | Sort-Object -Property CodeSize -Descending

    foreach ($functionInfo in $sortedFunctionInfos)
    {
        Write-Output $functionInfo | Select-Object -Property Name, CodeSize

        if ($ShowLayers -ne $true)
        {
            continue
        }

        $sortedLayerInfos = $functionInfo.Layers | Sort-Object -Property CodeSize -Descending

        foreach ($layer in $sortedLayerInfos)
        {
            Write-Output $layer
        }

        Write-Output "----"
    }
}

$functionNames = aws lambda list-functions `
    --query "Functions[?starts_with(FunctionName, '$Environment')].FunctionName" `
    --output json | ConvertFrom-Json

$functionInfos = GetFunctionInfos($functionNames)
DisplayFunctionInfos($functionInfos)
