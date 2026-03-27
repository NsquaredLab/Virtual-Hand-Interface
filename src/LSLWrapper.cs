using Godot;
using System;
using System.Linq;
using System.Reflection;

/// <summary>
/// Wrapper for SharpLSL that avoids static initialization hang in Godot.
/// Uses reflection to dynamically load and call LSL methods.
/// </summary>
public static class LSLWrapper
{
	private static Assembly lslAssembly;
	private static Type lslType;
	private static Type streamInfoType;
	private static Type streamInletType;
	private static Type streamOutletType;
	private static Type channelFormatType;
	private static Type transportOptionsType;

	private static bool initialized = false;

	/// <summary>
	/// Initialize LSL types via reflection. Call this once before using LSL.
	/// </summary>
	public static void Initialize()
	{
		if (initialized) return;

		try
		{
			lslAssembly = Assembly.Load("SharpLSL");
			lslType = lslAssembly.GetType("SharpLSL.LSL");
			streamInfoType = lslAssembly.GetType("SharpLSL.StreamInfo");
			streamInletType = lslAssembly.GetType("SharpLSL.StreamInlet");
			streamOutletType = lslAssembly.GetType("SharpLSL.StreamOutlet");
			channelFormatType = lslAssembly.GetType("SharpLSL.ChannelFormat");
			transportOptionsType = lslAssembly.GetType("SharpLSL.TransportOptions");

			initialized = true;
			GD.Print("✅ LSLWrapper initialized successfully");
		}
		catch (Exception e)
		{
			GD.PrintErr($"❌ Failed to initialize LSLWrapper: {e.Message}");
			throw;
		}
	}

	/// <summary>
	/// Resolve LSL streams by property (e.g., "name" or "type")
	/// Uses the simpler Resolve(maxCount, waitTime) and filters results
	/// </summary>
	public static object[] Resolve(string property, string value, double timeout = 1.0)
	{
		if (!initialized) Initialize();

		try
		{
			// Call LSL.Resolve(int maxCount, double waitTime) - simpler method that works
			var resolveMethod = lslType.GetMethod("Resolve", [typeof(int), typeof(double)]);

			if (resolveMethod == null)
			{
				GD.PrintErr("❌ Resolve method not found!");
				return [];
			}

			// Get all available streams
			object result = resolveMethod.Invoke(null, [1024, timeout]);

			if (result == null)
				return [];

			object[] allStreams = (object[])result;

			// Filter by property and value
			var filtered = new System.Collections.Generic.List<object>();
			foreach (var stream in allStreams)
			{
				try
				{
					string streamValue = null;
					if (property.ToLower() == "name")
					{
						streamValue = GetStreamInfoName(stream);
					}
					else if (property.ToLower() == "type")
					{
						var typeProperty = streamInfoType.GetProperty("Type");
						streamValue = (string)typeProperty.GetValue(stream);
					}

					if (streamValue != null && streamValue == value)
					{
						filtered.Add(stream);
					}
				}
				catch
				{
					// Skip streams we can't read
					continue;
				}
			}

			return [.. filtered];
		}
		catch (TargetInvocationException e)
		{
			// Get the inner exception for better error info
			if (e.InnerException != null)
			{
				GD.PrintErr($"❌ LSL Resolve failed: {e.InnerException.Message}");
				if (e.InnerException.InnerException != null)
				{
					GD.PrintErr($"   Root cause: {e.InnerException.InnerException.Message}");
				}
			}
			else
			{
				GD.PrintErr($"❌ LSL Resolve failed: {e.Message}");
			}
			return [];
		}
		catch (Exception e)
		{
			GD.PrintErr($"❌ LSL Resolve failed: {e.Message}");
			GD.PrintErr($"   Exception type: {e.GetType().Name}");
			return [];
		}
	}

	/// <summary>
	/// Create a StreamInfo object
	/// </summary>
	public static object CreateStreamInfo(string name, string type, int channelCount,
		double nominalSrate, string channelFormat, string sourceId)
	{
		if (!initialized) Initialize();

		try
		{
			// Get the ChannelFormat enum value
			object formatValue = Enum.Parse(channelFormatType, channelFormat);

			// Create StreamInfo
			var constructor = streamInfoType.GetConstructor([typeof(string), typeof(string), typeof(int), typeof(double), channelFormatType, typeof(string)]);

			return constructor.Invoke([name, type, channelCount, nominalSrate, formatValue, sourceId]);
		}
		catch (Exception e)
		{
			GD.PrintErr($"❌ Failed to create StreamInfo: {e.Message}");
			throw;
		}
	}

	/// <summary>
	/// Create a StreamInlet from a StreamInfo
	/// </summary>
	public static object CreateStreamInlet(object streamInfo, int maxChunkLength = 0, int maxBufferLength = 360, bool recover = true)
	{
		if (!initialized) Initialize();

		try
		{
			// Use the constructor: StreamInlet(StreamInfo streamInfo, Int32 maxChunkLength, Int32 maxBufferLength, Boolean recover, TransportOptions transportOptions)
			var constructor = streamInletType.GetConstructor([streamInfoType, typeof(int), typeof(int), typeof(bool), transportOptionsType]);

			if (constructor == null)
			{
				GD.PrintErr("❌ StreamInlet constructor not found!");
				throw new Exception("StreamInlet constructor not found");
			}

			// Create a default TransportOptions object
			object transportOptions = null;
			try
			{
				// Try to create TransportOptions with default constructor
				var transportOptionsConstructor = transportOptionsType.GetConstructor(Type.EmptyTypes);
				if (transportOptionsConstructor != null)
				{
					transportOptions = transportOptionsConstructor.Invoke([]);
				}
			}
			catch
			{
				// If we can't create it, pass null
			}

			return constructor.Invoke([streamInfo, maxChunkLength, maxBufferLength, recover, transportOptions]);
		}
		catch (Exception e)
		{
			GD.PrintErr($"❌ Failed to create StreamInlet: {e.Message}");
			throw;
		}
	}

	/// <summary>
	/// Create a StreamOutlet from a StreamInfo
	/// </summary>
	public static object CreateStreamOutlet(object streamInfo, int chunkSize = 0, int maxBuffered = 360)
	{
		if (!initialized) Initialize();

		try
		{
			// Use the constructor: StreamOutlet(StreamInfo streamInfo, Int32 chunkSize, Int32 maxBuffered, TransportOptions transportOptions)
			var constructor = streamOutletType.GetConstructor([streamInfoType, typeof(int), typeof(int), transportOptionsType]);

			if (constructor == null)
			{
				GD.PrintErr("❌ StreamOutlet constructor not found!");
				throw new Exception("StreamOutlet constructor not found");
			}

			// Create a default TransportOptions object
			object transportOptions = null;
			try
			{
				// Try to create TransportOptions with default constructor
				var transportOptionsConstructor = transportOptionsType.GetConstructor(Type.EmptyTypes);
				if (transportOptionsConstructor != null)
				{
					transportOptions = transportOptionsConstructor.Invoke([]);
					GD.Print("  Created default TransportOptions");
				}
			}
			catch (Exception ex)
			{
				// If we can't create it, pass null
				GD.Print($"  Using null for TransportOptions (couldn't create: {ex.Message})");
			}

			GD.Print($"  Creating StreamOutlet with chunkSize={chunkSize}, maxBuffered={maxBuffered}");
			return constructor.Invoke([streamInfo, chunkSize, maxBuffered, transportOptions]);
		}
		catch (Exception e)
		{
			GD.PrintErr($"❌ Failed to create StreamOutlet: {e.Message}");
			throw;
		}
	}

	/// <summary>
	/// Pull a sample from a StreamInlet (non-blocking)
	/// </summary>
	public static double PullSample(object inlet, float[] buffer, double timeout = 0.0)
	{
		try
		{
			var pullMethod = streamInletType.GetMethod("PullSample", [typeof(float[]), typeof(double)]);
			object result = pullMethod.Invoke(inlet, [buffer, timeout]);
			return (double)result;
		}
		catch (Exception e)
		{
			GD.PrintErr($"❌ PullSample failed: {e.Message}");
			return 0.0;
		}
	}

	/// <summary>
	/// Push a sample to a StreamOutlet
	/// </summary>
	public static void PushSample(object outlet, float[] sample)
	{
		try
		{
			var pushMethod = streamOutletType.GetMethod("PushSample", [typeof(float[])]);
			pushMethod.Invoke(outlet, [sample]);
		}
		catch (Exception e)
		{
			GD.PrintErr($"❌ PushSample failed: {e.Message}");
		}
	}

	/// <summary>
	/// Push a string sample to a StreamOutlet
	/// </summary>
	public static void PushSample(object outlet, string[] sample)
	{
		try
		{
			var pushMethod = streamOutletType.GetMethod("PushSample", [typeof(string[])]);
			pushMethod.Invoke(outlet, [sample]);
		}
		catch (Exception e)
		{
			GD.PrintErr($"❌ PushSample (string) failed: {e.Message}");
		}
	}

	/// <summary>
	/// Get StreamInfo name
	/// </summary>
	public static string GetStreamInfoName(object streamInfo)
	{
		try
		{
			var nameProperty = streamInfoType.GetProperty("Name");
			return (string)nameProperty.GetValue(streamInfo);
		}
		catch
		{
			return "Unknown";
		}
	}

	/// <summary>
	/// Get StreamInfo channel count
	/// </summary>
	public static int GetStreamInfoChannelCount(object streamInfo)
	{
		try
		{
			var channelCountProperty = streamInfoType.GetProperty("ChannelCount");
			return (int)channelCountProperty.GetValue(streamInfo);
		}
		catch
		{
			return 0;
		}
	}

	/// <summary>
	/// Set channel labels in StreamInfo description XML
	/// </summary>
	public static void SetChannelLabels(object streamInfo, string[] labels)
	{
		try
		{
			var descMethod = streamInfoType.GetMethod("get_Description");
			if (descMethod == null)
			{
				GD.PrintErr("SetChannelLabels: get_Description method not found");
				return;
			}

			object xmlElement = descMethod.Invoke(streamInfo, null);
			if (xmlElement == null)
			{
				GD.PrintErr("SetChannelLabels: Description is null");
				return;
			}

			var appendChildValue = xmlElement.GetType().GetMethod("AppendChild", [typeof(string), typeof(string)]);
			var appendChildEmpty = xmlElement.GetType().GetMethod("AppendChild", [typeof(string)]);

			if (appendChildEmpty == null)
			{
				GD.PrintErr("SetChannelLabels: AppendChild method not found");
				return;
			}

			// Create <channels> element
			object channelsElement = appendChildEmpty.Invoke(xmlElement, ["channels"]);

			// For each label, create <channel><label>Name</label></channel>
			var channelAppendEmpty = channelsElement.GetType().GetMethod("AppendChild", [typeof(string)]);
			var channelAppendValue = channelsElement.GetType().GetMethod("AppendChild", [typeof(string), typeof(string)]);

			foreach (string label in labels)
			{
				object channelElement = channelAppendEmpty.Invoke(channelsElement, ["channel"]);
				var labelAppend = channelElement.GetType().GetMethod("AppendChild", [typeof(string), typeof(string)]);
				labelAppend?.Invoke(channelElement, ["label", label]);
			}
		}
		catch (Exception e)
		{
			GD.PrintErr($"❌ Failed to set channel labels: {e.Message}");
		}
	}

	/// <summary>
	/// Add initial state/schema to StreamInfo metadata
	/// </summary>
	public static void SetStreamMetadata(object streamInfo, string fieldName, string value)
	{
		try
		{
			var descMethod = streamInfoType.GetMethod("get_Description");
			if (descMethod == null)
			{
				GD.PrintErr("SetStreamMetadata: get_Description method not found");
				return;
			}

			object xmlElement = descMethod.Invoke(streamInfo, null);
			if (xmlElement == null)
			{
				GD.PrintErr("SetStreamMetadata: Description is null");
				return;
			}

			var appendMethod = xmlElement.GetType().GetMethod("AppendChild", [typeof(string), typeof(string)]);
			if (appendMethod != null)
			{
				appendMethod.Invoke(xmlElement, [fieldName, value]);
			}
			else
			{
				GD.PrintErr("SetStreamMetadata: AppendChild method not found");
			}
		}
		catch (Exception e)
		{
			GD.PrintErr($"❌ Failed to set stream metadata: {e.Message}");
		}
	}

	/// <summary>
	/// Dispose an LSL object (StreamInlet or StreamOutlet)
	/// </summary>
	public static void Dispose(object lslObject)
	{
		if (lslObject == null) return;

		try
		{
			var disposeMethod = lslObject.GetType().GetMethod("Dispose", Type.EmptyTypes);
			if (disposeMethod != null)
			{
				disposeMethod.Invoke(lslObject, null);
			}
		}
		catch (Exception e)
		{
			GD.PrintErr($"❌ Dispose failed: {e.Message}");
		}
	}
}
