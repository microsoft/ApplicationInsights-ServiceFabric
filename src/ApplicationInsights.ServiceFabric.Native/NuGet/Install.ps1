param($installPath, $toolsPath, $package, $project)

$configFile = $project.ProjectItems.Item("ApplicationInsights.config")

# set 'Copy To Output Directory' to 'Copy if newer'
$copyToOutput = $configFile.Properties.Item("CopyToOutputDirectory")
$copyToOutput.Value = 2
