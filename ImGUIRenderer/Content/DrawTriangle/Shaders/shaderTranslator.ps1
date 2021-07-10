$binaries_path = "..\..\..\..\Binaries"
$hlsl_shaders = Get-ChildItem .\HLSL\Noesis -Recurse -Include ("*.fx")

foreach ($hlsl_path in $hlsl_shaders)
{
	$vk_path = $hlsl_path -Replace "HLSL", "VK" -Replace "fx", "spirv"
	$msl_path = $hlsl_path -Replace "HLSL", "MSL" -Replace "fx", "msl"
	$glsl_path = $hlsl_path -Replace "HLSL", "GLSL" -Replace "fx", "glsl"
	$essl_path = $hlsl_path -Replace "HLSL", "ESSL" -Replace "fx", "essl"

	# Generate output path
	New-Item -ItemType Directory -Force -Path (Split-Path -Path $vk_path) > $null
	New-Item -ItemType Directory -Force -Path (Split-Path -Path $msl_path) > $null
	New-Item -ItemType Directory -Force -Path (Split-Path -Path $glsl_path) > $null
	New-Item -ItemType Directory -Force -Path (Split-Path -Path $essl_path) > $null

	$is_vs = $hlsl_path.Name -Match "VS"

	& $binaries_path\dxc.exe -Zpr -fvk-u-shift 20 all -fvk-s-shift 40 all -fvk-t-shift 60 all -spirv $hlsl_path -T $(If ($is_vs) {"vs_5_0"} Else {"ps_5_0"}) -E main -Fo $vk_path #Vulkan
	& $binaries_path\SPIRV-Cross.exe --msl $vk_path --output $msl_path #Metal
	& $binaries_path\SPIRV-Cross.exe $vk_path --output $glsl_path #OpenGL
	& $binaries_path\SPIRV-Cross.exe --es --version 300 $vk_path --output $essl_path #OpenGLES
}

#Write-Host -NoNewLine 'Press any key to continue...';