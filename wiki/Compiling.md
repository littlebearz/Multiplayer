* Clone the repo into RimWorld's Mods folder (otherwise reference paths will need fixing)
* The code references a special publicized game assembly. You can use this to get it: https://gist.github.com/Zetrith/d86b1d84e993c8117983c09f1a5dcdcd (depends on [dnlib](https://github.com/0xd4d/dnlib))

Recommendation:
1.git clone https://github.com/CabbageCrow/AssemblyPublicizer.git
2.nuget update mono.cecil
3.nuget update mono.options
4.compile AssemblyPublicizer to exe
5.Copy "Assembly-CSharp.dll" into local working directory, the dll is located D:\Program Files (x86)\Steam\steamapps\common\RimWorld\RimWorldWin64_Data\Managed\ 

6.In cmd.exe run 
AssemblyPublicizer.exe -i "Assembly-CSharp.dll"
7.copy the new publicized dll into this folder D:\Program Files (x86)\Steam\steamapps\common\RimWorld\Mods\Multiplayer\Assemblies\Assembly-CSharp_public.dll