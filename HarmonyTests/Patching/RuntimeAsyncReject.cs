#if NET11_0_OR_GREATER
using System;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading.Tasks;
using HarmonyLib;
using NUnit.Framework;

namespace HarmonyLibTests.Patching
{
	[TestFixture, NonParallelizable]
	public class RuntimeAsyncReject : TestLogger
	{
		// The compiler refuses a manual Async flag, so build a flagged method at runtime.
		static MethodInfo BuildFlaggedMethod()
		{
			var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("RuntimeAsyncRejectTargets"), AssemblyBuilderAccess.Run);
			var module = assembly.DefineDynamicModule("RuntimeAsyncRejectTargets");
			var type = module.DefineType("Flagged", TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
			var method = type.DefineMethod("Flagged", MethodAttributes.Public | MethodAttributes.Static, typeof(Task), Type.EmptyTypes);
			method.SetImplementationFlags((MethodImplAttributes)0x2000);
			var il = method.GetILGenerator();
			il.Emit(OpCodes.Ldnull);
			il.Emit(OpCodes.Ret);
			return type.CreateType().GetMethod("Flagged");
		}

		[Test]
		public void Test_Patch_RuntimeAsyncTarget_Throws()
		{
			var target = BuildFlaggedMethod();
			var instance = new Harmony("test.reject.runtimeasync");

			var exception = Assert.Throws<NotSupportedException>(() =>
				instance.Patch(target, postfix: new HarmonyMethod(typeof(RuntimeAsyncReject), nameof(Postfix))));

			StringAssert.StartsWith("Runtime-async methods are not supported", exception.Message);
			Assert.IsNull(Harmony.GetPatchInfo(target));
		}

		[Test]
		public void Test_ReversePatch_RuntimeAsyncTarget_Throws()
		{
			var target = BuildFlaggedMethod();
			var instance = new Harmony("test.reject.runtimeasync.reverse");

			Assert.Throws<NotSupportedException>(() =>
				instance.CreateReversePatcher(target, new HarmonyMethod(typeof(RuntimeAsyncReject), nameof(Postfix))).Patch());
		}

		public static void Postfix() { }
	}
}
#endif
