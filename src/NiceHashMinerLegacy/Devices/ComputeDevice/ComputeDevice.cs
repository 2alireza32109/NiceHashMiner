﻿using System;
using NiceHashMiner.Configs;
using NiceHashMiner.Configs.Data;
using NiceHashMiner.Miners.Grouping;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using NiceHashMiner.Algorithms;
using NiceHashMiner.Devices.Algorithms;
using NiceHashMiner.Utils.Guid;
using NiceHashMinerLegacy.Common.Enums;

namespace NiceHashMiner.Devices
{
    public class ComputeDevice
    {
        public readonly int ID;

        public int Index { get; protected set; } // For socket control, unique

        // to identify equality;
        public readonly string Name; // { get; set; }

        // name count is the short name for displaying in moning groups
        public readonly string NameCount;
        public bool Enabled { get; protected set; }

        // disabled state check
        public bool IsDisabled => (!Enabled || State == DeviceState.Disabled);

        public DeviceState State { get; set; } = DeviceState.Stopped;

        public readonly DeviceGroupType DeviceGroupType;

        // CPU, NVIDIA, AMD
        public readonly DeviceType DeviceType;

        // UUID now used for saving
        public string Uuid { get; protected set; }

        public string B64Uuid
        {
            get
            {
                var type = DeviceType == DeviceType.CPU ? 1 : 2;
                var uuid = UUID.V5(UUID.Nil().AsGuid(), Uuid);
                var b64 = Convert.ToBase64String(uuid.AsGuid().ToByteArray());
                return $"{type}-{b64.Trim('=').Replace('/', '-')}";
            }
        }

        // used for Claymore indexing
        public int BusID { get; protected set; } = -1;
        public int IDByBus = -1;


        // CPU extras
        public int Threads { get; protected set; }
        public ulong AffinityMask { get; protected set; }

        // GPU extras
        public readonly ulong GpuRam;

        // sgminer extra quickfix
        //public readonly bool IsOptimizedVersion;
        public string Codename { get; protected set; }

        public string InfSection { get; protected set; }

        // amd has some algos not working with new drivers
        public bool DriverDisableAlgos { get; protected set; }

        protected List<Algorithm> AlgorithmSettings;

        public double MinimumProfit { get; set; }

        public string BenchmarkCopyUuid { get; set; }
        public string TuningCopyUuid { get; set; }

        public virtual float Load => -1;
        public virtual float Temp => -1;
        public virtual int FanSpeed => -1;
        public virtual double PowerUsage => -1;

        // Ambiguous constructor
        protected ComputeDevice(int id, string name, bool enabled, DeviceGroupType group,
            DeviceType type, string nameCount, ulong gpuRam)
        {
            ID = id;
            Name = name;
            SetEnabled(enabled);
            DeviceGroupType = group;
            DeviceType = type;
            NameCount = nameCount;
            GpuRam = gpuRam;
        }

        public void SetEnabled(bool isEnabled)
        {
            Enabled = isEnabled;
            State = isEnabled ? DeviceState.Stopped : DeviceState.Disabled;
        }

        // Fake dev
        public ComputeDevice(int id)
        {
            ID = id;
            Name = "fake_" + id;
            NameCount = Name;
            Enabled = true;
            DeviceType = DeviceType.CPU;
            DeviceGroupType = DeviceGroupType.NONE;
            //IsOptimizedVersion = false;
            Codename = "fake";
            Uuid = GetUuid(ID, GroupNames.GetGroupName(DeviceGroupType, ID), Name, DeviceGroupType);
            GpuRam = 0;
        }

        // combines long and short name
        public string GetFullName()
        {
            return string.Format(Translations.Tr("{0} {1}"), NameCount, Name);
        }

        public Algorithm GetAlgorithm(Algorithm modelAlgo)
        {
            return GetAlgorithm(modelAlgo.MinerBaseType, modelAlgo.NiceHashID, modelAlgo.SecondaryNiceHashID);
        }

        public Algorithm GetAlgorithm(MinerBaseType minerBaseType, AlgorithmType algorithmType,
            AlgorithmType secondaryAlgorithmType)
        {
            var toSetIndex = AlgorithmSettings.FindIndex(a =>
                a.NiceHashID == algorithmType && a.MinerBaseType == minerBaseType &&
                a.SecondaryNiceHashID == secondaryAlgorithmType);
            return toSetIndex > -1 ? AlgorithmSettings[toSetIndex] : null;
        }

        //public Algorithm GetAlgorithm(string algoID) {
        //    int toSetIndex = this.AlgorithmSettings.FindIndex((a) => a.AlgorithmStringID == algoID);
        //    if (toSetIndex > -1) {
        //        return this.AlgorithmSettings[toSetIndex];
        //    }
        //    return null;
        //}

        public void CopyBenchmarkSettingsFrom(ComputeDevice copyBenchCDev)
        {
            foreach (var copyFromAlgo in copyBenchCDev.AlgorithmSettings)
            {
                var setAlgo = GetAlgorithm(copyFromAlgo);
                if (setAlgo != null)
                {
                    setAlgo.BenchmarkSpeed = copyFromAlgo.BenchmarkSpeed;
                    setAlgo.ExtraLaunchParameters = copyFromAlgo.ExtraLaunchParameters;
                    setAlgo.LessThreads = copyFromAlgo.LessThreads;
                    setAlgo.PowerUsage = copyFromAlgo.PowerUsage;
                    if (setAlgo is DualAlgorithm dualSA && copyFromAlgo is DualAlgorithm dualCFA)
                    {
                        dualSA.SecondaryBenchmarkSpeed = dualCFA.SecondaryBenchmarkSpeed;
                    }
                }
            }
        }

        public void CopyTuningSettingsFrom(ComputeDevice copyTuningCDev)
        {
            foreach (var copyFromAlgo in copyTuningCDev.AlgorithmSettings.OfType<DualAlgorithm>())
            {
                if (GetAlgorithm(copyFromAlgo) is DualAlgorithm setAlgo)
                {
                    setAlgo.IntensitySpeeds = new Dictionary<int, double>(copyFromAlgo.IntensitySpeeds);
                    setAlgo.SecondaryIntensitySpeeds = new Dictionary<int, double>(copyFromAlgo.SecondaryIntensitySpeeds);
                    setAlgo.TuningStart = copyFromAlgo.TuningStart;
                    setAlgo.TuningEnd = copyFromAlgo.TuningEnd;
                    setAlgo.TuningInterval = copyFromAlgo.TuningInterval;
                    setAlgo.TuningEnabled = copyFromAlgo.TuningEnabled;
                    setAlgo.IntensityPowers = new Dictionary<int, double>(copyFromAlgo.IntensityPowers);
                    setAlgo.UseIntensityPowers = copyFromAlgo.UseIntensityPowers;
                    setAlgo.IntensityUpToDate = false;
                }
            }
        }

        #region Config Setters/Getters

        // settings
        // setters
        public virtual void SetFromComputeDeviceConfig(ComputeDeviceConfig config)
        {
            if (config != null && config.UUID == Uuid)
            {
                SetEnabled(config.Enabled);
                MinimumProfit = config.MinimumProfit;
            }
        }

        public void SetAlgorithmDeviceConfig(DeviceBenchmarkConfig config)
        {
            if (config != null && config.DeviceUUID == Uuid && config.AlgorithmSettings != null)
            {
                AlgorithmSettings = DefaultAlgorithms.GetAlgorithmsForDevice(this);
                foreach (var conf in config.AlgorithmSettings)
                {
                    var setAlgo = GetAlgorithm(conf.MinerBaseType, conf.NiceHashID, conf.SecondaryNiceHashID);
                    if (setAlgo != null)
                    {
                        setAlgo.BenchmarkSpeed = conf.BenchmarkSpeed;
                        setAlgo.ExtraLaunchParameters = conf.ExtraLaunchParameters;
                        setAlgo.Enabled = conf.Enabled;
                        setAlgo.LessThreads = conf.LessThreads;
                        setAlgo.PowerUsage = conf.PowerUsage;
                        if (setAlgo is DualAlgorithm dualSA)
                        {
                            dualSA.SecondaryBenchmarkSpeed = conf.SecondaryBenchmarkSpeed;
                            var dualConf = config.DualAlgorithmSettings?.Find(a =>
                                a.SecondaryNiceHashID == dualSA.SecondaryNiceHashID);
                            if (dualConf != null)
                            {
                                dualConf.FixSettingsBounds();
                                dualSA.IntensitySpeeds = dualConf.IntensitySpeeds;
                                dualSA.SecondaryIntensitySpeeds = dualConf.SecondaryIntensitySpeeds;
                                dualSA.TuningEnabled = dualConf.TuningEnabled;
                                dualSA.TuningStart = dualConf.TuningStart;
                                dualSA.TuningEnd = dualConf.TuningEnd;
                                dualSA.TuningInterval = dualConf.TuningInterval;
                                dualSA.IntensityPowers = dualConf.IntensityPowers;
                                dualSA.UseIntensityPowers = dualConf.UseIntensityPowers;
                            }
                        }
                    }
                }
            }
        }

        // getters
        public virtual ComputeDeviceConfig GetComputeDeviceConfig()
        {
            var ret = new ComputeDeviceConfig
            {
                Enabled = Enabled,
                Name = Name,
                UUID = Uuid,
                MinimumProfit = MinimumProfit
            };
            return ret;
        }

        public DeviceBenchmarkConfig GetAlgorithmDeviceConfig()
        {
            var ret = new DeviceBenchmarkConfig
            {
                DeviceName = Name,
                DeviceUUID = Uuid
            };
            // init algo settings
            foreach (var algo in AlgorithmSettings)
            {
                // create/setup
                var conf = new AlgorithmConfig
                {
                    Name = algo.AlgorithmStringID,
                    NiceHashID = algo.NiceHashID,
                    MinerBaseType = algo.MinerBaseType,
                    MinerName = algo.MinerName,
                    BenchmarkSpeed = algo.BenchmarkSpeed,
                    ExtraLaunchParameters = algo.ExtraLaunchParameters,
                    Enabled = algo.Enabled,
                    LessThreads = algo.LessThreads,
                    PowerUsage =  algo.PowerUsage
                };
                // insert
                ret.AlgorithmSettings.Add(conf);
                if (algo is DualAlgorithm dualAlgo)
                {
                    conf.SecondaryNiceHashID = dualAlgo.SecondaryNiceHashID;
                    conf.SecondaryBenchmarkSpeed = dualAlgo.SecondaryBenchmarkSpeed;

                    DualAlgorithmConfig dualConf = new DualAlgorithmConfig
                    {
                        Name = algo.AlgorithmStringID,
                        SecondaryNiceHashID = dualAlgo.SecondaryNiceHashID,
                        IntensitySpeeds = dualAlgo.IntensitySpeeds,
                        SecondaryIntensitySpeeds = dualAlgo.SecondaryIntensitySpeeds,
                        TuningEnabled = dualAlgo.TuningEnabled,
                        TuningStart = dualAlgo.TuningStart,
                        TuningEnd = dualAlgo.TuningEnd,
                        TuningInterval = dualAlgo.TuningInterval,
                        IntensityPowers = dualAlgo.IntensityPowers,
                        UseIntensityPowers = dualAlgo.UseIntensityPowers
                    };

                    ret.DualAlgorithmSettings.Add(dualConf);
                }
            }

            return ret;
        }

        #endregion Config Setters/Getters

        public List<Algorithm> GetAlgorithmSettings()
        {
            // hello state
            var algos = GetAlgorithmSettingsThirdParty(ConfigManager.GeneralConfig.Use3rdPartyMiners);

            var retAlgos = MinerPaths.GetAndInitAlgorithmsMinerPaths(algos, this);

            // sort by algo
            retAlgos.Sort((a_1, a_2) => (a_1.NiceHashID - a_2.NiceHashID) != 0
                ? (a_1.NiceHashID - a_2.NiceHashID)
                : ((a_1.MinerBaseType - a_2.MinerBaseType) != 0
                    ? (a_1.MinerBaseType - a_2.MinerBaseType)
                    : (a_1.SecondaryNiceHashID - a_2.SecondaryNiceHashID)));

            return retAlgos;
        }

        public List<Algorithm> GetAlgorithmSettingsFastest()
        {
            // hello state
            var algosTmp = GetAlgorithmSettings();
            var sortDict = new Dictionary<AlgorithmType, Algorithm>();
            foreach (var algo in algosTmp)
            {
                var algoKey = algo.NiceHashID;
                if (sortDict.ContainsKey(algoKey))
                {
                    if (sortDict[algoKey].BenchmarkSpeed < algo.BenchmarkSpeed)
                    {
                        sortDict[algoKey] = algo;
                    }
                }
                else
                {
                    sortDict[algoKey] = algo;
                }
            }

            return sortDict.Values.ToList();
        }

        private List<Algorithm> GetAlgorithmSettingsThirdParty(Use3rdPartyMiners use3rdParty)
        {
            if (use3rdParty == Use3rdPartyMiners.YES)
            {
                return AlgorithmSettings;
            }

            var thirdPartyMiners = new List<MinerBaseType>
            {
                MinerBaseType.Claymore,
                MinerBaseType.EWBF,
                MinerBaseType.Prospector,
                MinerBaseType.trex,
                MinerBaseType.Phoenix,
                MinerBaseType.GMiner,
                MinerBaseType.BMiner,
                MinerBaseType.TTMiner,
                MinerBaseType.NBMiner
            };

            return AlgorithmSettings.FindAll(a => thirdPartyMiners.IndexOf(a.MinerBaseType) == -1);
        }

        // static methods

        protected static string GetUuid(int id, string group, string name, DeviceGroupType deviceGroupType)
        {
            var sha256 = new SHA256Managed();
            var hash = new StringBuilder();
            var mixedAttr = id + group + name + (int) deviceGroupType;
            var hashedBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(mixedAttr), 0,
                Encoding.UTF8.GetByteCount(mixedAttr));
            foreach (var b in hashedBytes)
            {
                hash.Append(b.ToString("x2"));
            }

            // GEN indicates the UUID has been generated and cannot be presumed to be immutable
            return "GEN-" + hash;
        }

        internal bool IsAlgorithmSettingsInitialized()
        {
            return AlgorithmSettings != null;
        }

        public override bool Equals(object obj)
        {
            if (obj is null) return false;
            if (ReferenceEquals(this, obj)) return true;
            return obj.GetType() == GetType() && Equals((ComputeDevice) obj);
        }

        protected bool Equals(ComputeDevice other)
        {
            return ID == other.ID && DeviceGroupType == other.DeviceGroupType && DeviceType == other.DeviceType;
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = ID;
                hashCode = (hashCode * 397) ^ (int) DeviceGroupType;
                hashCode = (hashCode * 397) ^ (int) DeviceType;
                return hashCode;
            }
        }

        public static bool operator ==(ComputeDevice left, ComputeDevice right)
        {
            return Equals(left, right);
        }

        public static bool operator !=(ComputeDevice left, ComputeDevice right)
        {
            return !Equals(left, right);
        }
    }
}
