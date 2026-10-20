using System;
using System.ComponentModel;
using SRDebugger;
using SRDebugger.Internal;
using SRF.Service;
using UnityEngine;
using UnityEngine.Scripting;

[Preserve]
public class SROptions : INotifyPropertyChanged
{
	[AttributeUsage(AttributeTargets.Method | AttributeTargets.Property)]
	public sealed class DisplayNameAttribute : System.ComponentModel.DisplayNameAttribute
	{
		public DisplayNameAttribute(string displayName)
			: base(displayName)
		{
		}
	}

	[AttributeUsage(AttributeTargets.Property)]
	public sealed class IncrementAttribute : SRDebugger.IncrementAttribute
	{
		public IncrementAttribute(double increment)
			: base(increment)
		{
		}
	}

	[AttributeUsage(AttributeTargets.Property)]
	public sealed class NumberRangeAttribute : SRDebugger.NumberRangeAttribute
	{
		public NumberRangeAttribute(double min, double max)
			: base(min, max)
		{
		}
	}

	[AttributeUsage(AttributeTargets.Method | AttributeTargets.Property)]
	public sealed class SortAttribute : SRDebugger.SortAttribute
	{
		public SortAttribute(int priority)
			: base(priority)
		{
		}
	}

	private static SROptions _current;

	public static SROptions Current => _current;

	public event SROptionsPropertyChanged PropertyChanged;

	private event PropertyChangedEventHandler InterfacePropertyChangedEventHandler;

	event PropertyChangedEventHandler INotifyPropertyChanged.PropertyChanged
	{
		add
		{
			InterfacePropertyChangedEventHandler += value;
		}
		remove
		{
			InterfacePropertyChangedEventHandler -= value;
		}
	}

	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
	public static void OnStartup()
	{
		_current = new SROptions();
		SRServiceManager.GetService<InternalOptionsRegistry>().AddOptionContainer(Current);
	}

	public void OnPropertyChanged(string propertyName)
	{
		if (PropertyChanged != null)
		{
			PropertyChanged(this, propertyName);
		}
		if (InterfacePropertyChangedEventHandler != null)
		{
			InterfacePropertyChangedEventHandler(this, new PropertyChangedEventArgs(propertyName));
		}
	}
}
