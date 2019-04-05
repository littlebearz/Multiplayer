using System;

public class publicise
{
	public publicise()
	{
        private static void RewriteAssembly(string assemblyPath)
        {
            ModuleDef assembly = ModuleDefMD.Load(assemblyPath);

            foreach (TypeDef type in assembly.Types.SelectMany(t => t.NestedTypes.Concat(t)))
            {
                type.Attributes &= ~TypeAttributes.VisibilityMask;

                if (type.IsNested)
                {
                    type.Attributes |= TypeAttributes.NestedPublic;
                }
                else
                {
                    type.Attributes |= TypeAttributes.Public;
                }

                foreach (MethodDef method in type.Methods)
                {
                    method.Attributes &= ~MethodAttributes.MemberAccessMask;
                    method.Attributes |= MethodAttributes.Public;
                }

                foreach (FieldDef field in type.Fields)
                {
                    field.Attributes &= ~FieldAttributes.FieldAccessMask;
                    field.Attributes |= FieldAttributes.Public;
                }
            }

            assembly.Write($"{Path.ChangeExtension(assemblyPath, null)}_public.dll");
        }
    }
}
