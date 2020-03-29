﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using dnlib.DotNet;
using Harmony12;

namespace UnityModManagerNet
{
	public partial class UnityModManager
	{
		public partial class ModEntry
		{
			public class ModLogger
			{
				protected readonly string Prefix;
				protected readonly string PrefixError;
				protected readonly string PrefixCritical;
				protected readonly string PrefixWarning;
				protected readonly string PrefixException;

				public ModLogger(string Id)
				{
					Prefix = $"[{Id}] ";
					PrefixError = $"[{Id}] [Error] ";
					PrefixCritical = $"[{Id}] [Critical] ";
					PrefixWarning = $"[{Id}] [Warning] ";
					PrefixException = $"[{Id}] [Exception] ";
				}

				public void Log(string str)
				{
					UnityModManager.Logger.Log(str, Prefix);
				}

				public void Error(string str)
				{
					UnityModManager.Logger.Log(str, PrefixError);
				}

				public void Critical(string str)
				{
					UnityModManager.Logger.Log(str, PrefixCritical);
				}

				public void Warning(string str)
				{
					UnityModManager.Logger.Log(str, PrefixWarning);
				}

				public void NativeLog(string str)
				{
					UnityModManager.Logger.NativeLog(str, Prefix);
				}

				/// <summary>
				/// [0.17.0]
				/// </summary>
				public void LogException(string key, Exception e)
				{
					UnityModManager.Logger.LogException(key, e, PrefixException);
				}

				/// <summary>
				/// [0.17.0]
				/// </summary>
				public void LogException(Exception e)
				{
					UnityModManager.Logger.LogException(null, e, PrefixException);
				}
			}

			public readonly ModInfo Info;

			/// <summary>
			/// Path to mod folder
			/// </summary>
			public readonly string Path;

			Assembly mAssembly = null;
			public Assembly Assembly => mAssembly;

			/// <summary>
			/// Version of a mod
			/// </summary>
			public readonly Version Version = null;

			/// <summary>
			/// Required UMM version
			/// </summary>
			public readonly Version ManagerVersion = null;

			/// <summary>
			/// Required game version [0.15.0]
			/// </summary>
			public readonly Version GameVersion = null;

			/// <summary>
			/// Not used
			/// </summary>
			public Version NewestVersion;

			/// <summary>
			/// Required mods
			/// </summary>
			public readonly Dictionary<string, Version> Requirements = new Dictionary<string, Version>();

			/// <summary>
			/// Displayed in UMM UI. Add <color></color> tag to change colors. Can be used when custom verification game version [0.15.0]
			/// </summary>
			public string CustomRequirements = String.Empty;

			public readonly ModLogger Logger = null;

			/// <summary>
			/// Not used
			/// </summary>
			public bool HasUpdate = false;

			//public ModSettings Settings = null;

			/// <summary>
			/// Show button to reload the mod [0.14.0]
			/// </summary>
			public bool CanReload { get; private set; }

			/// <summary>
			/// Called to unload old data for reloading mod [0.14.0]
			/// </summary>
			public Func<ModEntry, bool> OnUnload = null;

			/// <summary>
			/// Called to activate / deactivate the mod
			/// </summary>
			public Func<ModEntry, bool, bool> OnToggle = null;

			/// <summary>
			/// Called by MonoBehaviour.OnGUI when mod options are visible.
			/// </summary>
			public Action<ModEntry> OnGUI = null;

			/// <summary>
			/// Called by MonoBehaviour.OnGUI, always [0.21.0]
			/// </summary>
			public Action<ModEntry> OnFixedGUI = null;

			/// <summary>
			/// Called when opening mod GUI [0.16.0]
			/// </summary>
			public Action<ModEntry> OnShowGUI = null;

			/// <summary>
			/// Called when closing mod GUI [0.16.0]
			/// </summary>
			public Action<ModEntry> OnHideGUI = null;

			/// <summary>
			/// Called when the game closes
			/// </summary>
			public Action<ModEntry> OnSaveGUI = null;

			/// <summary>
			/// Called by MonoBehaviour.Update [0.13.0]
			/// </summary>
			public Action<ModEntry, float> OnUpdate = null;

			/// <summary>
			/// Called by MonoBehaviour.LateUpdate [0.13.0]
			/// </summary>
			public Action<ModEntry, float> OnLateUpdate = null;

			/// <summary>
			/// Called by MonoBehaviour.FixedUpdate [0.13.0]
			/// </summary>
			public Action<ModEntry, float> OnFixedUpdate = null;

			Dictionary<long, MethodInfo> mCache = new Dictionary<long, MethodInfo>();

			bool mStarted = false;
			public bool Started => mStarted;

			bool mErrorOnLoading = false;
			public bool ErrorOnLoading => mErrorOnLoading;

			/// <summary>
			/// UI checkbox
			/// </summary>
			public bool Enabled = true;
			//public bool Enabled => Enabled;

			/// <summary>
			/// If OnToggle exists
			/// </summary>
			public bool Toggleable => OnToggle != null;

			/// <summary>
			/// If Assembly is loaded [0.13.1]
			/// </summary>
			public bool Loaded => Assembly != null;

			bool mFirstLoading = true;

			bool mActive = false;
			public bool Active
			{
				get => mActive;
				set
				{
					if (value && !Loaded)
					{
						var stopwatch = System.Diagnostics.Stopwatch.StartNew();
						Load();
						Logger.NativeLog($"Loading time {(stopwatch.ElapsedMilliseconds / 1000f):f2} s.");
						return;
					}

					if (!mStarted || mErrorOnLoading)
						return;

					try
					{
						if (value)
						{
							if (mActive)
								return;

							if (OnToggle == null || OnToggle(this, true))
							{
								mActive = true;
								this.Logger.Log($"Active.");
								GameScripts.OnModToggle(this, true);
							}
							else
							{
								this.Logger.Log($"Unsuccessfully.");
							}
						}
						else if (!forbidDisableMods)
						{
							if (!mActive)
								return;

							if (OnToggle != null && OnToggle(this, false))
							{
								mActive = false;
								this.Logger.Log($"Inactive.");
								GameScripts.OnModToggle(this, false);
							}
						}
					}
					catch (Exception e)
					{
						this.Logger.LogException("OnToggle", e);
					}
				}
			}

			public ModEntry(ModInfo info, string path)
			{
				Info = info;
				Path = path;
				Logger = new ModLogger(Info.Id);
				Version = ParseVersion(info.Version);
				ManagerVersion = !string.IsNullOrEmpty(info.ManagerVersion) ? ParseVersion(info.ManagerVersion) : new Version();
				GameVersion = !string.IsNullOrEmpty(info.GameVersion) ? ParseVersion(info.GameVersion) : new Version();

				if (info.Requirements != null && info.Requirements.Length > 0)
				{
					var regex = new Regex(@"(.*)-(\d\.\d\.\d).*");
					foreach (var id in info.Requirements)
					{
						var match = regex.Match(id);
						if (match.Success)
						{
							Requirements.Add(match.Groups[1].Value, ParseVersion(match.Groups[2].Value));
							continue;
						}
						if (!Requirements.ContainsKey(id))
							Requirements.Add(id, null);
					}
				}
			}
			public bool Load()
			{
				if (Loaded)
					return !mErrorOnLoading;

				mErrorOnLoading = false;

				this.Logger.Log($"Version '{Info.Version}'. Loading.");
				if (string.IsNullOrEmpty(Info.AssemblyName))
				{
					mErrorOnLoading = true;
					this.Logger.Error($"{nameof(Info.AssemblyName)} is null.");
				}

				if (string.IsNullOrEmpty(Info.EntryMethod))
				{
					mErrorOnLoading = true;
					this.Logger.Error($"{nameof(Info.EntryMethod)} is null.");
				}

				if (!string.IsNullOrEmpty(Info.ManagerVersion))
				{
					if (ManagerVersion > GetVersion())
					{
						mErrorOnLoading = true;
						this.Logger.Error($"Mod Manager must be version '{Info.ManagerVersion}' or higher.");
					}
				}

				if (!string.IsNullOrEmpty(Info.GameVersion))
				{
					if (gameVersion != VER_0 && GameVersion > gameVersion)
					{
						mErrorOnLoading = true;
						this.Logger.Error($"Game must be version '{Info.GameVersion}' or higher.");
					}
				}

				if (Requirements.Count > 0)
				{
					foreach (var item in Requirements)
					{
						var id = item.Key;
						var mod = FindMod(id);
						if (mod == null)
						{
							mErrorOnLoading = true;
							this.Logger.Error($"Required mod '{id}' missing.");
							continue;
						}
						else if (item.Value != null && item.Value > mod.Version)
						{
							mErrorOnLoading = true;
							this.Logger.Error($"Required mod '{id}' must be version '{item.Value}' or higher.");
							continue;
						}

						if (!mod.Active)
						{
							mod.Enabled = true;
							mod.Active = true;
							if (!mod.Active)
								this.Logger.Log($"Required mod '{id}' inactive.");
						}
					}
				}

				if (ErrorOnLoading)
					return false;

				string assemblyPath = System.IO.Path.Combine(Path, Info.AssemblyName);
				string pdbPath = assemblyPath.Replace(".dll", ".pdb");

				if (File.Exists(assemblyPath))
				{
					try
					{
						var assemblyCachePath = assemblyPath;
						var pdbCachePath = pdbPath;
						var cacheExists = false;

						if (mFirstLoading)
						{
							var fi = new FileInfo(assemblyPath);
							var hash = (ushort)((long)fi.LastWriteTimeUtc.GetHashCode() + version.GetHashCode() + ManagerVersion.GetHashCode()).GetHashCode();
							assemblyCachePath = assemblyPath + $".{hash}.cache";
							pdbCachePath = assemblyCachePath + ".pdb";
							cacheExists = File.Exists(assemblyCachePath);

							if (!cacheExists)
							{
								foreach (var filepath in Directory.GetFiles(Path, "*.cache"))
								{
									try
									{
										File.Delete(filepath);
									}
									catch (Exception)
									{
									}
								}
							}
						}

						if (ManagerVersion >= VER_0_13)
						{
							if (mFirstLoading)
							{
								if (!cacheExists)
								{
									File.Copy(assemblyPath, assemblyCachePath, true);
								}
								if (File.Exists(pdbPath))
								{
									File.Copy(pdbPath, pdbCachePath, true);
								}

								mAssembly = Assembly.LoadFile(assemblyCachePath);

								foreach (var type in mAssembly.GetTypes())
								{
									if (type.GetCustomAttributes(typeof(EnableReloadingAttribute), true).Any())
									{
										CanReload = true;
										break;
									}
								}
							}
							else
							{
								if (File.Exists(pdbPath))
								{
									mAssembly = Assembly.Load(File.ReadAllBytes(assemblyPath), File.ReadAllBytes(pdbPath));
								}
								else
								{
									mAssembly = Assembly.Load(File.ReadAllBytes(assemblyPath));
								}
							}
						}
						else
						{
							if (!cacheExists)
							{
								var modDef = ModuleDefMD.Load(File.ReadAllBytes(assemblyPath));
								foreach (var item in modDef.GetTypeRefs())
								{
									if (item.FullName == "UnityModManagerNet.UnityModManager")
									{
										item.ResolutionScope = new AssemblyRefUser(thisModuleDef.Assembly);
									}
								}
								foreach (var item in modDef.GetMemberRefs().Where(member => member.IsFieldRef))
								{
									if (item.Name == "modsPath" & item.Class.FullName == "UnityModManagerNet.UnityModManager")
									{
										item.Name = "OldModsPath";
									}
								}

								modDef.Write(assemblyCachePath);
							}

							mAssembly = Assembly.LoadFile(assemblyCachePath);
						}

						mFirstLoading = false;
					}
					catch (Exception exception)
					{
						mErrorOnLoading = true;
						this.Logger.Error($"Error loading file '{assemblyPath}'.");
						this.Logger.LogException(exception);
						return false;
					}


					try
					{
						object[] param = new object[] { this };
						Type[] types = new Type[] { typeof(ModEntry) };
						if (FindMethod(Info.EntryMethod, types, false) == null)
						{
							param = null;
							types = null;
						}

						if (!Invoke(Info.EntryMethod, out var result, param, types) || result != null && (bool)result == false)
						{
							mErrorOnLoading = true;
							this.Logger.Log($"Not loaded.");
						}
					}
					catch (Exception e)
					{
						mErrorOnLoading = true;
						this.Logger.Log(e.ToString());
						return false;
					}

					mStarted = true;

					if (!mErrorOnLoading)
					{
						Active = true;
						return true;
					}
				}
				else
				{
					mErrorOnLoading = true;
					this.Logger.Error($"File '{assemblyPath}' not found.");
				}

				return false;
			}


			internal void Reload()
			{
				if (!mStarted || !CanReload)
					return;

				try
				{
					string assemblyPath = System.IO.Path.Combine(Path, Info.AssemblyName);
					var reflAssembly = Assembly.ReflectionOnlyLoad(File.ReadAllBytes(assemblyPath));
					if (reflAssembly.GetName().Version == Assembly.GetName().Version)
					{
						this.Logger.Log("Reload is not needed. The version is exactly the same as the previous one.");
						return;
					}
				}
				catch (Exception e)
				{
					this.Logger.Error(e.ToString());
					return;
				}

				if (OnSaveGUI != null)
					OnSaveGUI.Invoke(this);

				this.Logger.Log("Reloading...");

				if (Toggleable)
				{
					var b = forbidDisableMods;
					forbidDisableMods = false;
					Active = false;
					forbidDisableMods = b;
				}
				else
				{
					mActive = false;
				}

				try
				{
					if (!Active && (OnUnload == null || OnUnload.Invoke(this)))
					{
						mCache.Clear();
						typeof(Harmony12.Traverse).GetField("Cache", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, new Harmony12.AccessCache());
						typeof(HarmonyLib.Traverse).GetField("Cache", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, HarmonyLib.AccessTools.CreateInstance(AccessTools.TypeByName("HarmonyLib.AccessCache")));

						var oldAssembly = Assembly;
						mAssembly = null;
						mStarted = false;
						mErrorOnLoading = false;

						OnToggle = null;
						OnGUI = null;
						OnFixedGUI = null;
						OnShowGUI = null;
						OnHideGUI = null;
						OnSaveGUI = null;
						OnUnload = null;
						OnUpdate = null;
						OnFixedUpdate = null;
						OnLateUpdate = null;
						CustomRequirements = null;

						if (Load())
						{
							var allTypes = oldAssembly.GetTypes();
							foreach (var type in allTypes)
							{
								var t = Assembly.GetType(type.FullName);
								if (t != null)
								{
									foreach (var field in type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
									{
										if (field.GetCustomAttributes(typeof(SaveOnReloadAttribute), true).Any())
										{
											var f = t.GetField(field.Name);
											if (f != null)
											{
												this.Logger.Log($"Copying field '{field.DeclaringType.Name}.{field.Name}'");
												try
												{
													if (field.FieldType != f.FieldType)
													{
														if (field.FieldType.IsEnum && f.FieldType.IsEnum)
														{
															f.SetValue(null, Convert.ToInt32(field.GetValue(null)));
														}
														else if (field.FieldType.IsClass && f.FieldType.IsClass)
														{
															//f.SetValue(null, Convert.ChangeType(field.GetValue(null), f.FieldType));
														}
														else if (field.FieldType.IsValueType && f.FieldType.IsValueType)
														{
															//f.SetValue(null, Convert.ChangeType(field.GetValue(null), f.FieldType));
														}
													}
													else
													{
														f.SetValue(null, field.GetValue(null));
													}
												}
												catch (Exception ex)
												{
													this.Logger.Error(ex.ToString());
												}
											}
										}
									}
								}
							}
						}
						return;
					}
					else if (Active)
					{
						this.Logger.Log("Must be deactivated.");
					}
				}
				catch (Exception e)
				{
					this.Logger.Error(e.ToString());
				}

				this.Logger.Log("Reloading canceled.");
			}

			public bool Invoke(string namespaceClassnameMethodname, out object result, object[] param = null, Type[] types = null)
			{
				result = null;
				try
				{
					var methodInfo = FindMethod(namespaceClassnameMethodname, types);
					if (methodInfo != null)
					{
						result = methodInfo.Invoke(null, param);
						return true;
					}
				}
				catch (Exception exception)
				{
					this.Logger.Error($"Error trying to call '{namespaceClassnameMethodname}'.");
					this.Logger.LogException(exception);
				}

				return false;
			}

			MethodInfo FindMethod(string namespaceClassnameMethodname, Type[] types, bool showLog = true)
			{
				long key = namespaceClassnameMethodname.GetHashCode();
				if (types != null)
				{
					foreach (var val in types)
					{
						key += val.GetHashCode();
					}
				}

				if (!mCache.TryGetValue(key, out var methodInfo))
				{
					if (mAssembly != null)
					{
						string classString = null;
						string methodString = null;
						var pos = namespaceClassnameMethodname.LastIndexOf('.');
						if (pos != -1)
						{
							classString = namespaceClassnameMethodname.Substring(0, pos);
							methodString = namespaceClassnameMethodname.Substring(pos + 1);
						}
						else
						{
							if (showLog)
								this.Logger.Error($"Function name error '{namespaceClassnameMethodname}'.");

							goto Exit;
						}
						var type = mAssembly.GetType(classString);
						if (type != null)
						{
							if (types == null)
								types = new Type[0];

							methodInfo = type.GetMethod(methodString, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, types, new ParameterModifier[0]);
							if (methodInfo == null)
							{
								if (showLog)
								{
									if (types.Length > 0)
									{
										this.Logger.Log($"Method '{namespaceClassnameMethodname}[{string.Join(", ", types.Select(x => x.Name).ToArray())}]' not found.");
									}
									else
									{
										this.Logger.Log($"Method '{namespaceClassnameMethodname}' not found.");
									}
								}
							}
						}
						else
						{
							if (showLog)
								this.Logger.Error($"Class '{classString}' not found.");
						}
					}
					else
					{
						if (showLog)
							UnityModManager.Logger.Error($"Can't find method '{namespaceClassnameMethodname}'. Mod '{Info.Id}' is not loaded.");
					}

				Exit:

					mCache[key] = methodInfo;
				}

				return methodInfo;
			}
		}
	}
}