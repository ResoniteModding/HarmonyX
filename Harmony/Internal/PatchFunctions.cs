using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib.Internal.Patching;
using HarmonyLib.Internal.RuntimeFixes;
using HarmonyLib.Internal.Util;
using HarmonyLib.Public.Patching;
using HarmonyLib.Tools;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using System.Text;
using MethodBody = Mono.Cecil.Cil.MethodBody;
using OpCodes = Mono.Cecil.Cil.OpCodes;

namespace HarmonyLib
{
	/// <summary>Patch function helpers</summary>
	internal static class PatchFunctions
	{
		/// <summary>Refuses runtime-async patch signatures that cannot exchange a result</summary>
		/// <param name="original">Method to be patched</param>
		/// <param name="prefixes">Prefix patches</param>
		/// <param name="postfixes">Postfix patches</param>
		/// <param name="finalizers">Finalizer patches</param>
		/// <param name="includeRegisteredPrefixes">Also compare against prefixes already registered on the original</param>
		///
		internal static void ValidateRuntimeAsyncResults(MethodBase original, IEnumerable<MethodInfo> prefixes, IEnumerable<MethodInfo> postfixes, IEnumerable<MethodInfo> finalizers, bool includeRegisteredPrefixes = true)
		{
			if (!AccessTools.IsRuntimeAsync(original)) return;

			var newPrefixes = prefixes is null ? new MethodInfo[0] : prefixes.Where(prefix => prefix is not null).ToArray();
			foreach (var prefix in newPrefixes)
			{
				ValidateRuntimeAsyncPrefix(original, prefix);
				ValidateRuntimeAsyncResult(original, prefix, HarmonyPatchType.Prefix);
			}

			var allPrefixes = includeRegisteredPrefixes
				? (original.GetPatchInfo()?.prefixes.Select(patch => patch.PatchMethod) ?? Enumerable.Empty<MethodInfo>()).Concat(newPrefixes)
				: newPrefixes;
			GetRuntimeAsyncResultChannelType(original, allPrefixes);

			foreach (var postfix in postfixes ?? Enumerable.Empty<MethodInfo>())
				ValidateRuntimeAsyncResult(original, postfix, HarmonyPatchType.Postfix);
			foreach (var finalizer in finalizers ?? Enumerable.Empty<MethodInfo>())
				ValidateRuntimeAsyncResult(original, finalizer, HarmonyPatchType.Finalizer);
		}

		/// <summary>Gets the awaitable __result channel shared by all prefixes, rejecting conflicting types</summary>
		/// <param name="original">Method to be patched</param>
		/// <param name="prefixes">Prefix patches to inspect</param>
		/// <returns>The awaitable type used as the shared __result channel, or null when no prefix declares one</returns>
		///
		static Type GetRuntimeAsyncResultChannelType(MethodBase original, IEnumerable<MethodInfo> prefixes)
		{
			Type channelType = null;
			MethodInfo channelPatch = null;
			foreach (var prefix in prefixes)
			{
				if (prefix is null) continue;

				var resultParameter = prefix.GetParameters().FirstOrDefault(parameter => parameter.Name == "__result");
				if (resultParameter is null) continue;

				var parameterType = resultParameter.ParameterType.IsByRef
					? resultParameter.ParameterType.GetElementType()
					: resultParameter.ParameterType;
				if (AccessTools.GetAwaitableType(parameterType, out _) == AccessTools.AwaitableType.None) continue;

				if (channelType is not null && channelType.FullName != parameterType.FullName)
					throw new InvalidHarmonyPatchArgumentException(
						$"Prefix {prefix.FullDescription()} declares {parameterType.FullName} __result, but prefix {channelPatch.FullDescription()} declares {channelType.FullName} __result",
						original, prefix);

				channelType = parameterType;
				channelPatch = prefix;
			}
			return channelType;
		}


		static void ValidateRuntimeAsyncResult(MethodBase original, MethodInfo method, HarmonyPatchType role)
		{
			if (method is null) return;

			var returnType = AccessTools.GetIlReturnType(original);
			foreach (var parameter in method.GetParameters())
			{
				if (parameter.Name != "__result") continue;

				var parameterType = parameter.ParameterType.IsByRef
					? parameter.ParameterType.GetElementType()
					: parameter.ParameterType;
				if (AccessTools.GetAwaitableType(parameterType, out var valueType) == AccessTools.AwaitableType.None) continue;

				if (role == HarmonyPatchType.Prefix)
				{
					if (!parameter.ParameterType.IsByRef)
						throw new InvalidHarmonyPatchArgumentException(
							$"Prefix patch {method.FullDescription()} declares __result as {parameterType.FullName}; declare it as ref {parameterType.FullName} __result",
							original, method);
					if (returnType == typeof(void))
					{
						if (valueType is not null)
							throw new InvalidHarmonyPatchArgumentException(
								$"{original.FullDescription()} returns void; remove the __result parameter from prefix {method.FullDescription()}",
								original, method);
						continue;
					}
					if (valueType is null || !returnType.IsAssignableFrom(valueType))
						throw new InvalidHarmonyPatchArgumentException(
							$"Cannot assign {parameterType.FullName} to __result of {original.FullDescription()}; declare ref Task<{returnType.FullName}> __result",
							original, method);
					continue;
				}

				if (role == HarmonyPatchType.Postfix)
				{
					if (parameter.ParameterType.IsByRef)
						throw new InvalidHarmonyPatchArgumentException(
							$"Runtime-async methods do not materialize their {parameterType.FullName}; declare __result as {parameterType.FullName} (without ref)",
							original, method);
					if (returnType == typeof(void))
					{
						if (valueType is not null)
							throw new InvalidHarmonyPatchArgumentException(
								$"{original.FullDescription()} returns void; remove the __result parameter from postfix {method.FullDescription()}",
								original, method);
						continue;
					}
					if (valueType is null || !valueType.IsAssignableFrom(returnType))
						throw new InvalidHarmonyPatchArgumentException(
							$"Cannot read {returnType.FullName} as {parameterType.FullName} in postfix {method.FullDescription()}; declare __result as Task<{returnType.FullName}>",
							original, method);
					continue;
				}

				throw new InvalidHarmonyPatchArgumentException(
					returnType == typeof(void)
						? $"Runtime-async methods do not materialize their {parameterType.FullName}; remove the __result parameter"
						: $"Runtime-async methods do not materialize their {parameterType.FullName}; declare __result as {returnType.FullName}",
					original, method);
			}
		}

		static void ValidateRuntimeAsyncPrefix(MethodBase original, MethodInfo method)
		{
			if (method is null) return;
			if (AccessTools.GetAwaitableType(method.ReturnType, out var awaitedType) == AccessTools.AwaitableType.None) return;
			if (awaitedType is null || awaitedType == typeof(bool)) return;

			var returnType = AccessTools.GetIlReturnType(original);
			var remedy = returnType == typeof(void)
				? "return `Task` or `Task<bool>`"
				: $"return `Task`, `Task<bool>` or declare `ref Task<{returnType.FullName}> __result`";
			throw new InvalidHarmonyPatchArgumentException(
				$"Prefix patch {method.FullDescription()} returns {method.ReturnType.FullName}, but {original.FullDescription()} returns {returnType.FullName}; {remedy}",
				original, method);
		}

		/// <summary>Sorts patch methods by their priority rules</summary>
		/// <param name="original">The original method</param>
		/// <param name="patches">Patches to sort</param>
		/// <param name="debug">Use debug mode</param>
		/// <returns>The sorted patch methods</returns>
		///
		internal static List<MethodInfo> GetSortedPatchMethods(MethodBase original, Patch[] patches, bool debug) => new PatchSorter(patches, debug).Sort(original);

		/// <summary>Sorts patch methods by their priority rules</summary>
		/// <param name="original">The original method</param>
		/// <param name="patches">Patches to sort</param>
		/// <param name="debug">Use debug mode</param>
		/// <returns>The sorted patch methods</returns>
		///
		internal static Patch[] GetSortedPatchMethodsAsPatches(MethodBase original, Patch[] patches, bool debug) => new PatchSorter(patches, debug).SortAsPatches(original);

		/// <summary>Creates new replacement method with the latest patches and detours the original method</summary>
		/// <param name="original">The original method</param>
		/// <param name="patchInfo">Information describing the patches</param>
		/// <returns>The newly created replacement method</returns>
		///
		internal static MethodInfo UpdateWrapper(MethodBase original, PatchInfo patchInfo)
		{
			var patcher = original.GetMethodPatcher();
			var dmd = patcher.PrepareOriginal();

			if (dmd != null)
			{
				var ctx = new ILContext(dmd.Definition);
				HarmonyManipulator.Manipulate(original, patchInfo, ctx);
			}

			try
			{
				return patcher.DetourTo(dmd?.Generate()) as MethodInfo;
			}
			catch (Exception ex)
			{
				throw HarmonyException.Create(ex, dmd?.Definition?.Body);
			}
		}

		internal static MethodInfo ReversePatch(HarmonyMethod standin, MethodBase original, MethodInfo postTranspiler, MethodInfo postManipulator)
		{
			if (standin is null)
				throw new ArgumentNullException(nameof(standin));
			if (standin.method is null)
				throw new ArgumentNullException(nameof(standin), $"{nameof(standin)}.{nameof(standin.method)} is NULL");
			if (!standin.method.IsStatic)
				throw new ArgumentException(nameof(standin), $"{nameof(standin)}.{nameof(standin.method)} is not static");

			var debug = standin.debug ?? false;
			var transpilers = new List<MethodInfo>();
			var ilmanipulators = new List<MethodInfo>();
			if (standin.reversePatchType == HarmonyReversePatchType.Snapshot)
			{
				var info = Harmony.GetPatchInfo(original);
				transpilers.AddRange(GetSortedPatchMethods(original, info.Transpilers.ToArray(), debug));
				ilmanipulators.AddRange(GetSortedPatchMethods(original, info.ILManipulators.ToArray(), debug));
			}
			if (postTranspiler is object) transpilers.Add(postTranspiler);
			if (postManipulator is object) ilmanipulators.Add(postManipulator);

			Logger.Log(Logger.LogChannel.Info, () =>
			{
				var sb = new StringBuilder();
				sb.AppendLine($"Reverse patching {standin.method.FullDescription()} with {original.FullDescription()}");
				static void PrintInfo(StringBuilder sb, ICollection<MethodInfo> methods, string name)
				{
					if (methods.Count <= 0) return;
					sb.AppendLine($"{name}:");
					foreach (var method in methods)
						sb.AppendLine($"  * {method.FullDescription()}");
				}
				PrintInfo(sb, transpilers, "Transpiler");
				PrintInfo(sb, ilmanipulators, "Manipulators");
				return sb.ToString();
			}, debug);

			MethodBody patchBody = null;
#pragma warning disable CA2000 // pinned with PatchTools.RememberObject later
			var hook = new ILHook(
#pragma warning restore CA2000
				standin.method, ctx =>
			{
				if (!(original is MethodInfo mi))
					return;

				patchBody = ctx.Body;

				var patcher = mi.GetMethodPatcher();
				var dmd = patcher.CopyOriginal();

				if (dmd == null)
					throw new NullReferenceException($"Cannot reverse patch {mi.FullDescription()}: method patcher ({patcher.GetType().FullDescription()}) can't copy original method body");

				var manipulator = new ILManipulator(dmd.Definition.Body, debug);

				// Copy over variables from the original code
				ctx.Body.Variables.Clear();
				foreach (var variableDefinition in manipulator.Body.Variables)
					ctx.Body.Variables.Add(new VariableDefinition(ctx.Module.ImportReference(variableDefinition.VariableType)));

				foreach (var methodInfo in transpilers)
					manipulator.AddTranspiler(methodInfo);

				manipulator.WriteTo(ctx.Body, standin.method);

				HarmonyManipulator.ApplyManipulators(ctx, original, ilmanipulators, null);

				StackTraceFixes.FixStackTrace(ctx);

				// Normalize rets in case they get removed
				Instruction retIns = null;
				foreach (var instruction in ctx.Instrs.Where(i => i.OpCode == OpCodes.Ret))
				{
					retIns ??= ctx.IL.Create(OpCodes.Ret);
					instruction.OpCode = OpCodes.Br;
					instruction.Operand = retIns;
				}
				if (retIns != null)
					ctx.IL.Append(retIns);

				// TODO: Handle new debugType
				Logger.Log(Logger.LogChannel.IL,
					() => $"Generated reverse patcher ({ctx.Method.FullName}):\n{ctx.Body.ToILDasmString()}", debug);
			}, applyByDefault: false);

			try
			{
				hook.Apply();
			}
			catch (Exception ex)
			{
				throw HarmonyException.Create(ex, patchBody);
			}

			var replacement = hook.Method as MethodInfo;
			PatchTools.RememberObject(standin.method, (replacement, hook));
			return replacement;
		}

		internal static IEnumerable<CodeInstruction> ApplyTranspilers(MethodBase methodBase, ILGenerator generator, int maxTranspilers = 0)
		{
			var patcher = methodBase.GetMethodPatcher();
			var dmd = patcher.CopyOriginal();

			if (dmd == null)
				throw new NullReferenceException($"Cannot reverse patch {methodBase.FullDescription()}: method patcher ({patcher.GetType().FullDescription()}) can't copy original method body");

			var manipulator = new ILManipulator(dmd.Definition.Body, false);

			var info = methodBase.GetPatchInfo();
			if (info is object)
			{
				var sortedTranspilers = GetSortedPatchMethods(methodBase, info.transpilers, false);
				for (var i = 0; i < maxTranspilers && i < sortedTranspilers.Count; i++)
					manipulator.AddTranspiler(sortedTranspilers[i]);
			}

			return manipulator.GetInstructions(generator, methodBase);
		}

		internal static void UnpatchConditional(Func<Patch, bool> executionCondition)
		{
			var originals = PatchProcessor.GetAllPatchedMethods().ToList(); // keep as is to avoid "Collection was modified"
			foreach (var original in originals)
			{
				var info = PatchProcessor.GetPatchInfo(original);
				var patchProcessor = new PatchProcessor(null, original);

				info.Postfixes.DoIf(executionCondition, patchInfo => patchProcessor.Unpatch(patchInfo.PatchMethod));
				info.Prefixes.DoIf(executionCondition, patchInfo => patchProcessor.Unpatch(patchInfo.PatchMethod));
				info.ILManipulators.DoIf(executionCondition, patchInfo => patchProcessor.Unpatch(patchInfo.PatchMethod));
				info.Transpilers.DoIf(executionCondition, patchInfo => patchProcessor.Unpatch(patchInfo.PatchMethod));
				info.Finalizers.DoIf(executionCondition, patchInfo => patchProcessor.Unpatch(patchInfo.PatchMethod));
			}
		}
	}
}
