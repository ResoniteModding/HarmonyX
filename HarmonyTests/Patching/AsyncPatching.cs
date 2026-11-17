#if NET452_OR_GREATER || NETCOREAPP
using System;
using System.Linq;
using System.Threading.Tasks;
using HarmonyLib;
using HarmonyLibTests.Assets;
using NUnit.Framework;
using TestLibrary;

namespace HarmonyLibTests.Patching
{
	[TestFixture, NonParallelizable]
	public class AsyncPatching : TestLogger
	{
		static async Task<int> ClassicSumAsync(int a, int b)
		{
			await Task.Yield();
			return a + b;
		}

		static async Task<int> ClassicSumPostfix(Task<int> __result)
		{
			return await __result;
		}

		static async Task<int> ClassicRejectAsync(int a)
		{
			await Task.Yield();
			return a;
		}

		static void ReplaceWith42(ref Task<int> __result) => __result = Task.FromResult(42);

		static void AssertEvents(params string[] expected)
		{
			CollectionAssert.AreEqual(expected, AsyncTargets.Events);
		}

		static InvalidHarmonyPatchArgumentException AssertRejected(Action action)
		{
			var exception = Assert.Throws<HarmonyException>(() => action());
			for (var inner = exception.InnerException; inner is not null; inner = inner.InnerException)
				if (inner is InvalidHarmonyPatchArgumentException invalid)
					return invalid;
			Assert.Fail($"Expected {nameof(InvalidHarmonyPatchArgumentException)} in {exception}");
			return null;
		}

		[Test]
		public void Test_ClassicAsyncPostfix_ChainsResult()
		{
			var instance = new Harmony("test.async.classicchain");
			_ = instance.Patch(
				AccessTools.Method(typeof(AsyncPatching), nameof(ClassicSumAsync), [typeof(int), typeof(int)]),
				postfix: AccessTools.Method(typeof(AsyncPatching), nameof(ClassicSumPostfix)));

			var result = ClassicSumAsync(2, 3).GetAwaiter().GetResult();

			Assert.AreEqual(5, result);
		}

#if NET11_0_OR_GREATER
		[TestCase(typeof(AsyncRefTaskOfTResultPatch), nameof(AsyncTargets.RefSumAsync))]
		[TestCase(typeof(AsyncRefTaskResultPatch), nameof(AsyncTargets.DoRefAsync))]
		[TestCase(typeof(AsyncRefValueTaskOfTResultPatch), nameof(AsyncTargets.RefSumValueAsync))]
		public void Test_AwaitableRefResult_IsRejected(Type patchType, string targetName)
		{
			var target = AccessTools.Method(typeof(AsyncTargets), targetName);
			var postfix = AccessTools.Method(patchType, "Postfix");

			var exception = Assert.Throws<InvalidHarmonyPatchArgumentException>(() =>
				new Harmony("test.async.reject.awaitableref").Patch(target, postfix: new HarmonyMethod(postfix)));

			StringAssert.Contains("without ref", exception.Message);

			var registered = Harmony.GetPatchInfo(target)?.Postfixes.Select(p => p.PatchMethod).ToArray() ?? [];
			CollectionAssert.DoesNotContain(registered, postfix);
		}

		[Test]
		public void Test_AsyncValueResult_ReceivesValue()
		{
			AsyncTargets.Reset();
			var instance = new Harmony("test.async.valueresult");
			try
			{
				instance.PatchAll(typeof(AsyncValueResultPatch));

				Assert.AreEqual(5, AsyncTargets.SumAsync(2, 3).GetAwaiter().GetResult());
				CollectionAssert.Contains(AsyncTargets.Events, "postfix:result:5");
			}
			finally
			{
				instance.UnpatchSelf();
			}
		}

		[Test]
		public void Test_AsyncRefValueResult_CanChangeValue()
		{
			AsyncTargets.Reset();
			var instance = new Harmony("test.async.refvalueresult");
			try
			{
				instance.PatchAll(typeof(AsyncRefValueResultPatch));

				Assert.AreEqual(6, AsyncTargets.RefSumAsync(2, 3).GetAwaiter().GetResult());
				CollectionAssert.Contains(AsyncTargets.Events, "postfix:before:5");
			}
			finally
			{
				instance.UnpatchSelf();
			}
		}

		[Test]
		public void Test_AsyncTaskOfTReturn_ReplacesValueAfterOriginal()
		{
			AsyncTargets.Reset();
			var instance = new Harmony("test.async.taskoftreturn");
			try
			{
				instance.PatchAll(typeof(AsyncTaskOfTReturnPatch));

				Assert.AreEqual(10, AsyncTargets.SumAsync(2, 3).GetAwaiter().GetResult());
				AssertEvents("original:start", "original:end", "postfix:start:5", "postfix:end");
			}
			finally
			{
				instance.UnpatchSelf();
			}
		}

		[Test]
		[Ignore("MonoMod cannot detour generic instantiations on .NET 11 (MonoMod#298, #341)")]
		public void Test_GenericSyncMethod_DummyDetour()
		{
			var target = AccessTools.Method(typeof(AsyncTargets), nameof(AsyncTargets.EchoSync)).MakeGenericMethod(typeof(int));
			Assert.False(AccessTools.IsRuntimeAsync(target));
			var instance = new Harmony("test.sync.generic.dummy");
			try
			{
				instance.Patch(target);

				Assert.AreEqual(7, target.Invoke(null, new object[] { 7 }));
			}
			finally
			{
				instance.UnpatchSelf();
			}
		}

		[Test]
		[Ignore("MonoMod cannot detour generic instantiations on .NET 11 (MonoMod#298, #341)")]
		public void Test_GenericAsyncMethod_DummyDetour()
		{
			var target = AccessTools.Method(typeof(AsyncTargets), nameof(AsyncTargets.EchoAsync)).MakeGenericMethod(typeof(int));
			Assert.True(AccessTools.IsRuntimeAsync(target));
			var instance = new Harmony("test.async.generic.dummy");
			try
			{
				instance.Patch(target);

				Assert.AreEqual(7, ((Task<int>)target.Invoke(null, new object[] { 7 })).GetAwaiter().GetResult());
			}
			finally
			{
				instance.UnpatchSelf();
			}
		}

		[Test]
		public void Test_SyncIntReturn_ReplacesValue()
		{
			AsyncTargets.Reset();
			var instance = new Harmony("test.async.intreturn");
			try
			{
				instance.PatchAll(typeof(AsyncIntReturnPatch));

				Assert.AreEqual(10, AsyncTargets.SumAsync(2, 3).GetAwaiter().GetResult());
			}
			finally
			{
				instance.UnpatchSelf();
			}
		}

		[Test]
		public void Test_PatchWhileSuspended_ResumesOriginalVersion()
		{
			InFlightTargets.Reset();
			var gate = InFlightTargets.Gate;
			var first = InFlightTargets.RunAsync(1);
			Assert.AreEqual("start", InFlightTargets.Events[0]);

			var instance = new Harmony("test.async.inflight");
			try
			{
				instance.PatchAll(typeof(InFlightPatch));

				gate.SetResult(10);
				Assert.AreEqual(11, first.GetAwaiter().GetResult());
				CollectionAssert.DoesNotContain(InFlightTargets.Events, "postfix:11");

				InFlightTargets.Reset();
				var second = InFlightTargets.RunAsync(2);
				InFlightTargets.Gate.SetResult(10);
				Assert.AreEqual(12, second.GetAwaiter().GetResult());
				CollectionAssert.Contains(InFlightTargets.Events, "postfix:12");
			}
			finally
			{
				instance.UnpatchSelf();
			}
		}

		[Test]
		public void Test_AsyncValueTaskOfTReturn_ReplacesValue()
		{
			AsyncTargets.Reset();
			var instance = new Harmony("test.async.valuetaskoftreturn");
			try
			{
				instance.PatchAll(typeof(AsyncValueTaskOfTReturnPatch));

				Assert.AreEqual(10, AsyncTargets.SumValueAsync(2, 3).GetAwaiter().GetResult());
				CollectionAssert.Contains(AsyncTargets.Events, "postfix:end");
			}
			finally
			{
				instance.UnpatchSelf();
			}
		}

		[Test]
		public void Test_MethodTypeAsync_OnRuntimeAsync_IsRejected()
		{
			var instance = new Harmony("test.async.methodtype.reject");

			var exception = Assert.Throws<HarmonyException>(() => instance.PatchAll(typeof(AsyncMethodTypePatch)));

			Assert.IsInstanceOf<NotSupportedException>(exception.InnerException);
			StringAssert.Contains("no state machine", exception.InnerException.Message);
		}

		[Test]
		public void Test_AsyncBoolPrefix_SkipsOriginal()
		{
			AsyncTargets.Reset();
			var instance = new Harmony("test.async.boolprefix.skip");
			try
			{
				instance.PatchAll(typeof(AsyncBoolPrefixPatch));

				AsyncTargets.DoAsync().GetAwaiter().GetResult();

				AssertEvents("prefix:start", "prefix:end");
			}
			finally
			{
				instance.UnpatchSelf();
			}
		}

		[Test]
		public void Test_AsyncBoolPrefix_RunsOriginalWhenTrue()
		{
			AsyncTargets.Reset();
			var instance = new Harmony("test.async.boolprefix.run");
			try
			{
				instance.PatchAll(typeof(AsyncBoolPrefixContinuePatch));

				AsyncTargets.DoAsync().GetAwaiter().GetResult();

				AssertEvents("prefix:start", "prefix:end", "original:start", "original:end");
			}
			finally
			{
				instance.UnpatchSelf();
			}
		}

		[Test]
		public void Test_AsyncValueTaskBoolPrefix_SkipsOriginal()
		{
			AsyncPrefixTargets.Reset();
			var instance = new Harmony("test.async.valuetaskboolprefix.skip");
			try
			{
				instance.PatchAll(typeof(AsyncValueTaskBoolPrefixPatch));

				AsyncPrefixTargets.DoValueAsync().GetAwaiter().GetResult();

				CollectionAssert.AreEqual(new[] { "prefix:start", "prefix:end" }, AsyncPrefixTargets.Events);
			}
			finally
			{
				instance.UnpatchSelf();
			}
		}

		[Test]
		public void Test_AsyncVoidPrefix_AwaitsBeforeOriginal()
		{
			AsyncTargets.Reset();
			var instance = new Harmony("test.async.voidprefix");
			try
			{
				instance.PatchAll(typeof(AsyncVoidPrefixPatch));

				AsyncTargets.DoAsync().GetAwaiter().GetResult();

				AssertEvents("prefix:start", "prefix:end", "original:start", "original:end");
			}
			finally
			{
				instance.UnpatchSelf();
			}
		}

		[Test]
		public void Test_AsyncIntPrefix_IsRejected()
		{
			var target = AccessTools.Method(typeof(AsyncPrefixTargets), nameof(AsyncPrefixTargets.DoRejectAsync));
			var prefix = AccessTools.Method(typeof(AsyncIntPrefix), nameof(AsyncIntPrefix.Prefix));

			var exception = Assert.Throws<InvalidHarmonyPatchArgumentException>(() =>
				new Harmony("test.async.intprefix.reject").Patch(target, prefix: new HarmonyMethod(prefix)));

			StringAssert.Contains("`Task<bool>`", exception.Message);
			var registered = Harmony.GetPatchInfo(target)?.Prefixes.Select(p => p.PatchMethod).ToArray() ?? [];
			CollectionAssert.DoesNotContain(registered, prefix);
		}

		[Test]
		public void Test_AsyncInvalidResultPrefix_OnVoidPatchAll_RejectedBeforeRegistration()
		{
			var target = AccessTools.Method(typeof(AsyncPrefixTargets), nameof(AsyncPrefixTargets.DoRejectAsync));
			Assert.True(AccessTools.IsRuntimeAsync(target));
			var prefix = AccessTools.Method(typeof(AsyncInvalidResultPrefixPatch),
				nameof(AsyncInvalidResultPrefixPatch.Prefix));

			var exception = AssertRejected(() =>
				new Harmony("test.async.invalidresult.patchall").PatchAll(typeof(AsyncInvalidResultPrefixPatch)));

			StringAssert.Contains("System.Void", exception.Message);
			StringAssert.Contains("`Task<bool>`", exception.Message);
			var registered = Harmony.GetPatchInfo(target)?.Prefixes.Select(p => p.PatchMethod).ToArray() ?? [];
			CollectionAssert.DoesNotContain(registered, prefix);
		}

		[Test]
		public void Test_AsyncCombinedPrefix_SkipsOriginalAndReturnsResult()
		{
			AsyncTargets.Reset();
			var instance = new Harmony("test.async.combined.skip");
			try
			{
				instance.PatchAll(typeof(AsyncCombinedPrefixSkipPatch));

				var result = AsyncTargets.SumAsync(2, 3).GetAwaiter().GetResult();

				Assert.AreEqual(100, result);
				CollectionAssert.AreEqual(new[] { "prefix:start", "prefix:end" }, AsyncTargets.Events);
			}
			finally
			{
				instance.UnpatchSelf();
			}
		}

		[Test]
		public void Test_AsyncCombinedPrefix_TrueRunsOriginal()
		{
			AsyncTargets.Reset();
			var instance = new Harmony("test.async.combined.run");
			try
			{
				instance.PatchAll(typeof(AsyncCombinedPrefixRunPatch));

				var result = AsyncTargets.SumAsync(2, 3).GetAwaiter().GetResult();

				Assert.AreEqual(5, result);
				AssertEvents("prefix:start", "prefix:end", "original:start", "original:end");
			}
			finally
			{
				instance.UnpatchSelf();
			}
		}

		[Test]
		public void Test_AsyncValueTaskCombinedPrefix_ReturnsResult()
		{
			AsyncTargets.Reset();
			var instance = new Harmony("test.async.combined.valuetask");
			try
			{
				instance.PatchAll(typeof(AsyncValueTaskCombinedPrefixPatch));

				Assert.AreEqual(200, AsyncTargets.SumValueAsync(2, 3).GetAwaiter().GetResult());
			}
			finally
			{
				instance.UnpatchSelf();
			}
		}

		[Test]
		public void Test_AsyncCombinedPrefix_BoxesResultForReferenceReturnType()
		{
			AsyncPrefixTargets.Reset();
			var instance = new Harmony("test.async.combined.boxed");
			try
			{
				instance.PatchAll(typeof(AsyncCombinedBoxedPrefixPatch));

				var result = AsyncPrefixTargets.BoxAsync().GetAwaiter().GetResult();

				Assert.IsInstanceOf<int>(result);
				Assert.AreEqual(7, result);
				CollectionAssert.DoesNotContain(AsyncPrefixTargets.Events, "original:start");
			}
			finally
			{
				instance.UnpatchSelf();
			}
		}

		[Test]
		public void Test_AsyncCombinedPrefix_OnBoolReturningOriginal_ControlsAndSetsResult()
		{
			AsyncPrefixTargets.Reset();
			var instance = new Harmony("test.async.combined.bool");
			try
			{
				instance.PatchAll(typeof(AsyncCombinedBoolPrefixPatch));

				var result = AsyncPrefixTargets.TryCheckAsync(false).GetAwaiter().GetResult();

				Assert.IsTrue(result);
				CollectionAssert.DoesNotContain(AsyncPrefixTargets.Events, "original:start");
			}
			finally
			{
				instance.UnpatchSelf();
			}
		}

		[Test]
		public void Test_AsyncCombinedPrefix_WrongElement_IsRejected()
		{
			var target = AccessTools.Method(typeof(AsyncPrefixTargets), nameof(AsyncPrefixTargets.SumRejectAsync));
			var prefix = AccessTools.Method(typeof(AsyncCombinedStringPrefix),
				nameof(AsyncCombinedStringPrefix.Prefix));

			var exception = Assert.Throws<InvalidHarmonyPatchArgumentException>(() =>
				new Harmony("test.async.combined.string").Patch(target, prefix: new HarmonyMethod(prefix)));

			StringAssert.Contains(typeof(int).FullName, exception.Message);
			var registered = Harmony.GetPatchInfo(target)?.Prefixes.Select(p => p.PatchMethod).ToArray() ?? [];
			CollectionAssert.DoesNotContain(registered, prefix);
		}

		[Test]
		public void Test_AsyncCombinedPrefix_OnVoid_IsRejected()
		{
			var target = AccessTools.Method(typeof(AsyncPrefixTargets), nameof(AsyncPrefixTargets.DoRejectAsync));
			var prefix = AccessTools.Method(typeof(AsyncCombinedVoidPrefixPatch),
				nameof(AsyncCombinedVoidPrefixPatch.Prefix));

			var exception = Assert.Throws<InvalidHarmonyPatchArgumentException>(() =>
				new Harmony("test.async.combined.void").Patch(target, prefix: new HarmonyMethod(prefix)));

			StringAssert.Contains("System.Void", exception.Message);
			StringAssert.Contains("`Task<bool>`", exception.Message);
		}

		[Test]
		public void Test_AsyncRefTaskPrefix_ReplacesResultAndSkipsOriginal()
		{
			AsyncTargets.Reset();
			var instance = new Harmony("test.async.reftask.skip");
			try
			{
				instance.PatchAll(typeof(AsyncRefTaskPrefix));

				var result = AsyncTargets.SumAsync(2, 3).GetAwaiter().GetResult();

				Assert.AreEqual(100, result);
				CollectionAssert.DoesNotContain(AsyncTargets.Events, "original:start");
			}
			finally
			{
				instance.UnpatchSelf();
			}
		}

		[Test]
		public void Test_AsyncRefTaskPrefix_TrueRunsOriginal()
		{
			AsyncTargets.Reset();
			var instance = new Harmony("test.async.reftask.run");
			try
			{
				instance.PatchAll(typeof(AsyncRefTaskRunPrefix));

				var result = AsyncTargets.SumAsync(2, 3).GetAwaiter().GetResult();

				Assert.AreEqual(5, result);
				AssertEvents("original:start", "original:end");
			}
			finally
			{
				instance.UnpatchSelf();
			}
		}

		[Test]
		public void Test_AsyncRefValueTaskPrefix_ReplacesResult()
		{
			AsyncTargets.Reset();
			var instance = new Harmony("test.async.refvaluetask.skip");
			try
			{
				instance.PatchAll(typeof(AsyncRefValueTaskPrefix));

				Assert.AreEqual(200, AsyncTargets.SumValueAsync(2, 3).GetAwaiter().GetResult());
			}
			finally
			{
				instance.UnpatchSelf();
			}
		}

		[Test]
		public void Test_AsyncRefVoidTaskPrefix_AwaitsAndSkipsOriginal()
		{
			AsyncTargets.Reset();
			var instance = new Harmony("test.async.refvoidtask.skip");
			try
			{
				instance.PatchAll(typeof(AsyncRefVoidTaskPrefix));

				AsyncTargets.DoAsync().GetAwaiter().GetResult();

				AssertEvents("prefix:ref");
			}
			finally
			{
				instance.UnpatchSelf();
			}
		}

		[Test]
		public void Test_AsyncRefBoolTaskPrefix_ReplacesBoolResult()
		{
			AsyncPrefixTargets.Reset();
			var instance = new Harmony("test.async.refbool.skip");
			try
			{
				instance.PatchAll(typeof(AsyncRefBoolTaskPrefix));

				Assert.IsTrue(AsyncPrefixTargets.TryCheckAsync(false).GetAwaiter().GetResult());
				CollectionAssert.DoesNotContain(AsyncPrefixTargets.Events, "original:start");
			}
			finally
			{
				instance.UnpatchSelf();
			}
		}

		[Test]
		public void Test_AsyncBoolPrefix_OnBoolReturningOriginal_StaysControl()
		{
			AsyncPrefixTargets.Reset();
			var instance = new Harmony("test.async.boolcontrol");
			try
			{
				instance.PatchAll(typeof(AsyncBoolControlPrefix));

				var result = AsyncPrefixTargets.TryCheckAsync(true).GetAwaiter().GetResult();

				Assert.IsFalse(result);
				CollectionAssert.AreEqual(new[] { "prefix:start", "prefix:end" }, AsyncPrefixTargets.Events);
			}
			finally
			{
				instance.UnpatchSelf();
			}
		}

		[Test]
		public void Test_AsyncRefTaskPrefix_BoxesResultForReferenceReturnType()
		{
			AsyncPrefixTargets.Reset();
			var instance = new Harmony("test.async.refboxed");
			try
			{
				instance.PatchAll(typeof(AsyncRefBoxedTaskPrefix));

				var result = AsyncPrefixTargets.BoxAsync().GetAwaiter().GetResult();

				Assert.IsInstanceOf<int>(result);
				Assert.AreEqual(7, result);
				CollectionAssert.DoesNotContain(AsyncPrefixTargets.Events, "original:start");
			}
			finally
			{
				instance.UnpatchSelf();
			}
		}

		[Test]
		public void Test_AsyncRefTaskPrefix_BoxesResultForInterfaceReturnType()
		{
			AsyncPrefixTargets.Reset();
			var instance = new Harmony("test.async.refboxedinterface");
			try
			{
				instance.PatchAll(typeof(AsyncRefBoxedInterfaceTaskPrefix));

				var result = AsyncPrefixTargets.FormatAsync().GetAwaiter().GetResult();

				Assert.IsInstanceOf<int>(result);
				Assert.AreEqual(9, result);
			}
			finally
			{
				instance.UnpatchSelf();
			}
		}

		[Test]
		public void Test_AsyncRefTaskPrefix_KeepsReferenceResult()
		{
			AsyncPrefixTargets.Reset();
			var instance = new Harmony("test.async.reftext");
			try
			{
				instance.PatchAll(typeof(AsyncRefTextTaskPrefix));

				var result = AsyncPrefixTargets.TextAsync("original").GetAwaiter().GetResult();

				Assert.AreEqual("prefix", result);
				CollectionAssert.DoesNotContain(AsyncPrefixTargets.Events, "original:start");
			}
			finally
			{
				instance.UnpatchSelf();
			}
		}

		[Test]
		public void Test_AsyncRefTaskPrefix_WrongElement_IsRejected()
		{
			var target = AccessTools.Method(typeof(AsyncPrefixTargets), nameof(AsyncPrefixTargets.SumRejectAsync));
			var prefix = AccessTools.Method(typeof(AsyncRefTaskStringPrefix), nameof(AsyncRefTaskStringPrefix.Prefix));

			var exception = Assert.Throws<InvalidHarmonyPatchArgumentException>(() =>
				new Harmony("test.async.refstring.reject").Patch(target, prefix: new HarmonyMethod(prefix)));

			StringAssert.Contains(typeof(int).FullName, exception.Message);
			var registered = Harmony.GetPatchInfo(target)?.Prefixes.Select(p => p.PatchMethod).ToArray() ?? [];
			CollectionAssert.DoesNotContain(registered, prefix);
		}

		[Test]
		public void Test_AsyncRefTaskPostfix_IsRejected()
		{
			var target = AccessTools.Method(typeof(AsyncTargets), nameof(AsyncTargets.SumAsync));
			var postfix = AccessTools.Method(typeof(AsyncRefTaskPostfix), nameof(AsyncRefTaskPostfix.Postfix));

			var exception = Assert.Throws<InvalidHarmonyPatchArgumentException>(() =>
				new Harmony("test.async.refpostfix.reject").Patch(target, postfix: new HarmonyMethod(postfix)));

			StringAssert.Contains("without ref", exception.Message);
			var registered = Harmony.GetPatchInfo(target)?.Postfixes.Select(p => p.PatchMethod).ToArray() ?? [];
			CollectionAssert.DoesNotContain(registered, postfix);
		}

		[Test]
		public void Test_AwaitableResultPostfix_ReceivesValue()
		{
			AsyncTargets.Reset();
			var instance = new Harmony("test.async.awaitablepostfix");
			try
			{
				instance.PatchAll(typeof(AsyncTaskOfTResultPatch));

				Assert.AreEqual(5, AsyncTargets.SumAsync(2, 3).GetAwaiter().GetResult());
				CollectionAssert.Contains(AsyncTargets.Events, "postfix:result:5");
			}
			finally
			{
				instance.UnpatchSelf();
			}
		}

		[Test]
		public void Test_AwaitableVoidResultPostfix_ReceivesCompletedTask()
		{
			AsyncTargets.Reset();
			var instance = new Harmony("test.async.awaitablevoidpostfix");
			try
			{
				instance.PatchAll(typeof(AsyncTaskResultPatch));

				AsyncTargets.DoAsync().GetAwaiter().GetResult();

				AssertEvents("original:start", "original:end", "postfix:start", "postfix:end");
			}
			finally
			{
				instance.UnpatchSelf();
			}
		}

		[Test]
		public void Test_AwaitableValueTaskResultPostfix_ReceivesValue()
		{
			AsyncTargets.Reset();
			var instance = new Harmony("test.async.awaitablevaluetaskpostfix");
			try
			{
				instance.PatchAll(typeof(AsyncValueTaskOfTResultPatch));

				Assert.AreEqual(5, AsyncTargets.SumValueAsync(2, 3).GetAwaiter().GetResult());
				CollectionAssert.Contains(AsyncTargets.Events, "postfix:result:5");
			}
			finally
			{
				instance.UnpatchSelf();
			}
		}

		[Test]
		public void Test_AsyncIntPrefix_OnClassicMethod_IsRejected()
		{
			var target = AccessTools.Method(typeof(AsyncPatching), nameof(ClassicRejectAsync));
			Assert.False(AccessTools.IsRuntimeAsync(target));
			var prefix = AccessTools.Method(typeof(AsyncIntPrefix), nameof(AsyncIntPrefix.Prefix));

			var exception = AssertRejected(() =>
				new Harmony("test.async.intprefix.classic").Patch(target, prefix: new HarmonyMethod(prefix)));

			StringAssert.Contains("only runtime-async methods can await async prefixes", exception.Message);
		}

		[Test]
		public void Test_AsyncBoolPrefix_OnClassicMethod_IsRejected()
		{
			var target = AccessTools.Method(typeof(AsyncPatching), nameof(ClassicRejectAsync));
			Assert.False(AccessTools.IsRuntimeAsync(target));
			var prefix = AccessTools.Method(typeof(AsyncBoolPrefixPatch), nameof(AsyncBoolPrefixPatch.Prefix));

			var exception = AssertRejected(() =>
				new Harmony("test.async.boolprefix.classic").Patch(target, prefix: new HarmonyMethod(prefix)));

			StringAssert.Contains("only runtime-async methods can await async prefixes", exception.Message);
		}

		[Test]
		public void Test_AsyncVoidPostfix_RunsAfterOriginalCompletes()
		{
			AsyncTargets.Reset();
			var instance = new Harmony("test.async.voidpostfix");
			try
			{
				instance.PatchAll(typeof(RuntimeAsyncVoidPostfix));

				AsyncTargets.DoOtherAsync().GetAwaiter().GetResult();

				AssertEvents("original:start", "original:end", "postfix:start", "postfix:end");
			}
			finally
			{
				instance.UnpatchSelf();
			}
		}

		// MonoMod detours the runtime-async variant too (MonoMod PR 346, addresses issue 344), so the patch
		// must be visible to a caller whose async variant is compiled only after the patch is installed
		[Test]
		public void Test_OrdinaryTaskMethod_SeenByRuntimeAsyncCaller()
		{
			var original = AccessTools.Method(typeof(OrdinaryTaskTarget), nameof(OrdinaryTaskTarget.Ordinary));
			var nativeCaller = AccessTools.Method(typeof(OrdinaryTaskTarget), nameof(OrdinaryTaskTarget.NativeCaller));
			Assert.False(AccessTools.IsRuntimeAsync(original));
			Assert.True(AccessTools.IsRuntimeAsync(nativeCaller));

			var instance = new Harmony("test.async.ordinary");
			try
			{
				instance.Patch(original, postfix: new HarmonyMethod(AccessTools.Method(typeof(AsyncPatching), nameof(ReplaceWith42))));

				Assert.AreEqual(42, OrdinaryTaskTarget.Ordinary(5).GetAwaiter().GetResult());
				Assert.AreEqual(42, OrdinaryTaskTarget.NativeCaller(5).GetAwaiter().GetResult());
			}
			finally
			{
				instance.UnpatchSelf();
			}

			Assert.AreEqual(6, OrdinaryTaskTarget.Ordinary(5).GetAwaiter().GetResult());
			Assert.AreEqual(6, OrdinaryTaskTarget.NativeCaller(5).GetAwaiter().GetResult());
		}

		[Test]
		public void Test_OrdinaryVirtualMethod_SeenByRuntimeAsyncCaller()
		{
			var derive = new OrdinaryVirtualDerived();
			Assert.AreEqual(7, OrdinaryVirtualCaller.NativeVirtualCaller(derive, 5).GetAwaiter().GetResult());

			var instance = new Harmony("test.async.ordinaryvirtual");
			try
			{
				instance.Patch(
					AccessTools.Method(typeof(OrdinaryVirtualDerived), nameof(OrdinaryVirtualDerived.Compute)),
					postfix: new HarmonyMethod(AccessTools.Method(typeof(AsyncPatching), nameof(ReplaceWith42))));

				Assert.AreEqual(42, derive.Compute(5).GetAwaiter().GetResult());
				Assert.AreEqual(42, OrdinaryVirtualCaller.NativeVirtualCaller(derive, 5).GetAwaiter().GetResult());
			}
			finally
			{
				instance.UnpatchSelf();
			}

			Assert.AreEqual(7, derive.Compute(5).GetAwaiter().GetResult());
			Assert.AreEqual(7, OrdinaryVirtualCaller.NativeVirtualCaller(derive, 5).GetAwaiter().GetResult());
		}

		[Test]
		public void Test_RuntimeAsyncReflection()
		{
			var method = AccessTools.Method(typeof(AsyncTargets), nameof(AsyncTargets.SumAsync));

			Assert.True(AccessTools.IsRuntimeAsync(method));
			Assert.AreEqual(typeof(Task<int>), AccessTools.GetReturnedType(method));
			// Runtime-async methods have no state machine, so MethodType.Async cannot resolve them. Patch the method itself.
			Assert.IsNull(AccessTools.AsyncMoveNext(method));
		}
#endif
	}
}
#endif
