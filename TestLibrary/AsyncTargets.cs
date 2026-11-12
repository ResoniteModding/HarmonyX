#if NET452_OR_GREATER || NETCOREAPP
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace TestLibrary;

public static class AsyncTargets
{
	public static readonly List<string> Events = new();

	public static void Reset() => Events.Clear();

	public static async Task<int> SumAsync(int a, int b)
	{
		Events.Add("original:start");
		await Task.Yield();
		Events.Add("original:end");
		return a + b;
	}

	public static async Task<int> RefSumAsync(int a, int b)
	{
		await Task.Yield();
		return a + b;
	}

#if NETCOREAPP
	public static async ValueTask<int> SumValueAsync(int a, int b)
	{
		await Task.Yield();
		return a + b;
	}

	public static async ValueTask<int> RefSumValueAsync(int a, int b)
	{
		await Task.Yield();
		return a + b;
	}

	public static async ValueTask DoValueAsync()
	{
		await Task.Yield();
	}
#endif

	public static async Task DoAsync()
	{
		Events.Add("original:start");
		await Task.Yield();
		Events.Add("original:end");
	}

	public static async Task DoRefAsync()
	{
		await Task.Yield();
	}

	public static async Task DoOtherAsync()
	{
		Events.Add("original:start");
		await Task.Yield();
		Events.Add("original:end");
	}
}

#if NET11_0_OR_GREATER
public static class OrdinaryTaskTarget
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static Task<int> Ordinary(int value) => Task.FromResult(value + 1);

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static async Task<int> NativeCaller(int value) => await Ordinary(value);
}

public class OrdinaryVirtualBase
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public virtual Task<int> Compute(int value) => Task.FromResult(value + 1);
}

public sealed class OrdinaryVirtualDerived : OrdinaryVirtualBase
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public override Task<int> Compute(int value) => Task.FromResult(value + 2);
}

public static class OrdinaryVirtualCaller
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static async Task<int> NativeVirtualCaller(OrdinaryVirtualBase target, int value) => await target.Compute(value);
}
#endif
#endif
