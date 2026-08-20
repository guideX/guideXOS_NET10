using System;

namespace GuideXOS.Net10.ManagedKernel;

internal enum ManagedDriverMatchType : uint
{
    ExactVendorDevice = 1,
    ClassSubclassProgrammingInterface = 2,
    ClassSubclass = 3,
    Class = 4
}

internal enum ManagedDriverBindingState : uint
{
    Unbound = 0,
    Matched = 1,
    Bound = 2
}

internal readonly struct ManagedDriverMatchRule
{
    internal readonly ManagedDriverMatchType Type;
    internal readonly ushort VendorId;
    internal readonly ushort DeviceId;
    internal readonly byte ClassCode;
    internal readonly byte Subclass;
    internal readonly byte ProgrammingInterface;

    internal ManagedDriverMatchRule(ManagedDriverMatchType type,
                                    ushort vendorId = 0,
                                    ushort deviceId = 0,
                                    byte classCode = 0,
                                    byte subclass = 0,
                                    byte programmingInterface = 0)
    {
        Type = type;
        VendorId = vendorId;
        DeviceId = deviceId;
        ClassCode = classCode;
        Subclass = subclass;
        ProgrammingInterface = programmingInterface;
    }
}

internal readonly struct ManagedDriverDefinition
{
    internal readonly uint DriverId;
    internal readonly uint NameToken;
    internal readonly int Priority;
    internal readonly ManagedDriverMatchRule[] Rules;

    internal ManagedDriverDefinition(uint driverId, uint nameToken, int priority,
                                     ManagedDriverMatchRule[] rules)
    {
        DriverId = driverId;
        NameToken = nameToken;
        Priority = priority;
        Rules = rules;
    }
}

internal readonly struct ManagedDriverBindingInfo
{
    internal readonly ManagedDriverBindingState State;
    internal readonly uint DriverId;
    internal readonly uint NameToken;
    internal readonly ManagedDriverMatchType MatchType;
    internal readonly uint Specificity;
    internal readonly int Priority;
    internal readonly uint RegistrationOrder;

    internal ManagedDriverBindingInfo(ManagedDriverBindingState state,
                                      uint driverId, uint nameToken,
                                      ManagedDriverMatchType matchType,
                                      uint specificity, int priority,
                                      uint registrationOrder)
    {
        State = state;
        DriverId = driverId;
        NameToken = nameToken;
        MatchType = matchType;
        Specificity = specificity;
        Priority = priority;
        RegistrationOrder = registrationOrder;
    }
}

internal unsafe sealed class ManagedDriverRegistry
{
    internal const uint MaxDrivers = 8;
    internal const uint MaxRulesPerDriver = 4;
    internal const uint MaxTotalRules = 16;
    internal const int MinPriority = -1000;
    internal const int MaxPriority = 1000;

    private const uint StateUnbound = (uint)ManagedDriverBindingState.Unbound;
    private const uint StateMatched = (uint)ManagedDriverBindingState.Matched;
    private const uint StateBound = (uint)ManagedDriverBindingState.Bound;

    private struct DriverRecord
    {
        internal uint DriverId;
        internal uint NameToken;
        internal int Priority;
        internal uint RuleStart;
        internal uint RuleCount;
        internal uint RegistrationOrder;
        internal uint Reserved;
    }

    private struct RuleRecord
    {
        internal uint Type;
        internal ushort VendorId;
        internal ushort DeviceId;
        internal byte ClassCode;
        internal byte Subclass;
        internal byte ProgrammingInterface;
        internal byte Reserved0;
        internal uint Reserved1;
    }

    private struct BindingRecord
    {
        internal uint State;
        internal uint DriverId;
        internal uint NameToken;
        internal uint MatchType;
        internal uint Specificity;
        internal int Priority;
        internal uint RegistrationOrder;
        internal uint Reserved;
    }

    private struct Candidate
    {
        internal bool Found;
        internal uint DriverIndex;
        internal uint RuleIndex;
        internal uint Specificity;
        internal int Priority;
        internal uint RegistrationOrder;
    }

    private readonly KernelArena _arena;
    private readonly KernelArenaAllocation _driverStorage;
    private readonly KernelArenaAllocation _ruleStorage;
    private readonly KernelArenaAllocation _bindingStorage;
    private readonly DriverRecord* _drivers;
    private readonly RuleRecord* _rules;
    private readonly BindingRecord* _bindings;
    private uint _driverCount;
    private uint _ruleCount;
    private uint _boundDeviceCount;
    private uint _unboundDeviceCount;
    private bool _frozen;
    private bool _bound;
    private bool _destroyed;
    private ManagedDeviceInventory? _boundInventory;

    private ManagedDriverRegistry(KernelArena arena,
                                  in KernelArenaAllocation driverStorage,
                                  in KernelArenaAllocation ruleStorage,
                                  in KernelArenaAllocation bindingStorage)
    {
        _arena = arena;
        _driverStorage = driverStorage;
        _ruleStorage = ruleStorage;
        _bindingStorage = bindingStorage;
        _drivers = (DriverRecord*)(nuint)driverStorage.VirtualAddress;
        _rules = (RuleRecord*)(nuint)ruleStorage.VirtualAddress;
        _bindings = (BindingRecord*)(nuint)bindingStorage.VirtualAddress;
        _driverCount = 0;
        _ruleCount = 0;
        _boundDeviceCount = 0;
        _unboundDeviceCount = 0;
        _frozen = false;
        _bound = false;
        _destroyed = false;
        _boundInventory = null;
    }

    internal static ManagedDriverRegistry? Create(IKernelMemoryProvider provider)
    {
        if (provider == null || !provider.IsAvailable ||
            KernelArena.TryCreate(provider, 2, 2, 4, 8, 8, 4096,
                                  out KernelArena? arena) != KernelArenaStatus.Ok ||
            arena == null)
        {
            return null;
        }

        KernelArenaAllocation driverStorage = default;
        KernelArenaAllocation ruleStorage = default;
        KernelArenaAllocation bindingStorage = default;
        ulong driverBytes = (ulong)MaxDrivers * (ulong)sizeof(DriverRecord);
        ulong ruleBytes = (ulong)MaxTotalRules * (ulong)sizeof(RuleRecord);
        ulong bindingBytes = (ulong)ManagedDeviceInventory.MaxDevices *
                             (ulong)sizeof(BindingRecord);
        if (arena.TryAllocate(driverBytes, 8, out driverStorage) != KernelArenaStatus.Ok ||
            arena.TryAllocate(ruleBytes, 8, out ruleStorage) != KernelArenaStatus.Ok ||
            arena.TryAllocate(bindingBytes, 8, out bindingStorage) != KernelArenaStatus.Ok)
        {
            if (driverStorage.AllocationId != 0) arena.Free(in driverStorage);
            if (ruleStorage.AllocationId != 0) arena.Free(in ruleStorage);
            if (bindingStorage.AllocationId != 0) arena.Free(in bindingStorage);
            arena.Destroy();
            return null;
        }

        ManagedDriverRegistry candidate = new ManagedDriverRegistry(
            arena, in driverStorage, in ruleStorage, in bindingStorage);
        for (uint index = 0; index != MaxDrivers; ++index)
        {
            candidate._drivers[index] = default;
        }
        for (uint index = 0; index != MaxTotalRules; ++index)
        {
            candidate._rules[index] = default;
        }
        for (uint index = 0; index != ManagedDeviceInventory.MaxDevices; ++index)
        {
            candidate._bindings[index] = default;
        }
        return candidate.ValidateInvariants() ? candidate : null;
    }

    internal bool IsDestroyed => _destroyed;
    internal bool IsFrozen => !_destroyed && _frozen;
    internal bool IsBound => !_destroyed && _bound;
    internal uint DriverCount => _destroyed ? 0U : _driverCount;
    internal uint TotalRuleCount => _destroyed ? 0U : _ruleCount;
    internal uint BoundDeviceCount => _destroyed ? 0U : _boundDeviceCount;
    internal uint UnboundDeviceCount => _destroyed ? 0U : _unboundDeviceCount;
    internal KernelArenaMetrics Metrics => _destroyed ? default : _arena.GetMetrics();

    internal bool TryRegister(in ManagedDriverDefinition definition)
    {
        if (_destroyed || _frozen || _bound || _driverCount >= MaxDrivers ||
            definition.DriverId == 0 || definition.NameToken == 0 ||
            definition.Priority < MinPriority || definition.Priority > MaxPriority ||
            definition.Rules == null || definition.Rules.Length == 0 ||
            definition.Rules.Length > MaxRulesPerDriver ||
            _ruleCount > MaxTotalRules - (uint)definition.Rules.Length)
        {
            return false;
        }
        for (uint index = 0; index != _driverCount; ++index)
        {
            if (_drivers[index].DriverId == definition.DriverId) return false;
        }
        for (int index = 0; index != definition.Rules.Length; ++index)
        {
            if (!ValidateRule(in definition.Rules[index])) return false;
        }

        DriverRecord record = new()
        {
            DriverId = definition.DriverId,
            NameToken = definition.NameToken,
            Priority = definition.Priority,
            RuleStart = _ruleCount,
            RuleCount = (uint)definition.Rules.Length,
            RegistrationOrder = _driverCount + 1,
            Reserved = 0
        };
        _drivers[_driverCount] = record;
        for (uint index = 0; index != record.RuleCount; ++index)
        {
            ManagedDriverMatchRule source = definition.Rules[index];
            _rules[_ruleCount + index] = new RuleRecord
            {
                Type = (uint)source.Type,
                VendorId = source.VendorId,
                DeviceId = source.DeviceId,
                ClassCode = source.ClassCode,
                Subclass = source.Subclass,
                ProgrammingInterface = source.ProgrammingInterface,
                Reserved0 = 0,
                Reserved1 = 0
            };
        }
        _driverCount++;
        _ruleCount += record.RuleCount;
        return ValidateInvariants();
    }

    internal bool TryFreeze()
    {
        if (_destroyed || _bound || _frozen || _driverCount == 0 ||
            _ruleCount == 0 || !ValidateInvariants()) return false;
        _frozen = true;
        return ValidateInvariants();
    }

    internal bool TryBind(ManagedDeviceInventory inventory)
    {
        if (_destroyed || !_frozen || _bound || inventory == null ||
            inventory.IsDestroyed || inventory.DeviceCount == 0 ||
            inventory.DeviceCount > ManagedDeviceInventory.MaxDevices ||
            !inventory.ValidateInvariants()) return false;

        for (uint index = 0; index != inventory.DeviceCount; ++index)
        {
            if (!inventory.TryGetDevice(index, out ManagedDevice device)) return false;
            Candidate candidate = SelectBest(in device);
            BindingRecord binding = new() { State = StateUnbound };
            if (candidate.Found)
            {
                DriverRecord driver = _drivers[candidate.DriverIndex];
                RuleRecord rule = _rules[candidate.RuleIndex];
                binding.State = StateMatched;
                binding.DriverId = driver.DriverId;
                binding.NameToken = driver.NameToken;
                binding.MatchType = rule.Type;
                binding.Specificity = candidate.Specificity;
                binding.Priority = candidate.Priority;
                binding.RegistrationOrder = candidate.RegistrationOrder;
                binding.Reserved = 0;
                binding.State = StateBound;
                _boundDeviceCount++;
            }
            else
            {
                _unboundDeviceCount++;
            }
            _bindings[index] = binding;
        }
        for (uint index = inventory.DeviceCount; index != ManagedDeviceInventory.MaxDevices; ++index)
        {
            _bindings[index] = default;
        }
        _boundInventory = inventory;
        _bound = true;
        if (!ValidateInvariants())
        {
            _bound = false;
            _boundInventory = null;
            _boundDeviceCount = 0;
            _unboundDeviceCount = 0;
            for (uint index = 0; index != ManagedDeviceInventory.MaxDevices; ++index)
            {
                _bindings[index] = default;
            }
            return false;
        }
        return true;
    }

    internal bool TryGetBinding(uint deviceIndex, out ManagedDriverBindingInfo info)
    {
        info = default;
        if (_destroyed || !_bound || _boundInventory == null ||
            deviceIndex >= _boundInventory.DeviceCount) return false;
        BindingRecord binding = _bindings[deviceIndex];
        info = new ManagedDriverBindingInfo(
            (ManagedDriverBindingState)binding.State, binding.DriverId,
            binding.NameToken, (ManagedDriverMatchType)binding.MatchType,
            binding.Specificity, binding.Priority, binding.RegistrationOrder);
        return binding.State == StateUnbound || binding.State == StateBound;
    }

    internal bool IsDeviceBound(uint deviceIndex)
    {
        return TryGetBinding(deviceIndex, out ManagedDriverBindingInfo info) &&
               info.State == ManagedDriverBindingState.Bound;
    }

    internal bool TryGetBoundDriver(uint deviceIndex, out uint driverId)
    {
        driverId = 0;
        return TryGetBinding(deviceIndex, out ManagedDriverBindingInfo info) &&
               info.State == ManagedDriverBindingState.Bound &&
               (driverId = info.DriverId) != 0;
    }

    internal bool TryGetDevicesBoundToDriver(uint driverId, uint[] output,
                                             out uint count)
    {
        count = 0;
        if (_destroyed || !_bound || _boundInventory == null || driverId == 0 ||
            output == null || output.Length < _boundDeviceCount) return false;
        for (uint index = 0; index != _boundInventory.DeviceCount; ++index)
        {
            if (_bindings[index].State != StateBound ||
                _bindings[index].DriverId != driverId) continue;
            output[count++] = index;
        }
        return true;
    }

    internal bool ValidateInvariants()
    {
        if (_destroyed || !_arena.ValidateInvariants() ||
            _arena.LiveAllocationCount != 3 || _driverCount > MaxDrivers ||
            _ruleCount > MaxTotalRules ||
            (_bound && (_boundInventory == null || _boundInventory.IsDestroyed)))
        {
            return false;
        }
        uint countedRules = 0;
        for (uint index = 0; index != _driverCount; ++index)
        {
            DriverRecord driver = _drivers[index];
            if (driver.DriverId == 0 || driver.NameToken == 0 ||
                driver.Priority < MinPriority || driver.Priority > MaxPriority ||
                driver.RuleCount == 0 || driver.RuleCount > MaxRulesPerDriver ||
                driver.RuleStart != countedRules ||
                driver.RuleStart > _ruleCount - driver.RuleCount ||
                driver.RegistrationOrder != index + 1 || driver.Reserved != 0)
            {
                return false;
            }
            for (uint rule = 0; rule != driver.RuleCount; ++rule)
            {
                if (!ValidateRule(in _rules[driver.RuleStart + rule])) return false;
            }
            for (uint prior = 0; prior != index; ++prior)
            {
                if (_drivers[prior].DriverId == driver.DriverId) return false;
            }
            countedRules += driver.RuleCount;
        }
        if (countedRules != _ruleCount) return false;
        if (!_bound) return _boundDeviceCount == 0 && _unboundDeviceCount == 0;
        if (_boundInventory == null ||
            _boundDeviceCount + _unboundDeviceCount != _boundInventory.DeviceCount)
        {
            return false;
        }
        uint bound = 0;
        uint unbound = 0;
        for (uint index = 0; index != _boundInventory.DeviceCount; ++index)
        {
            if (!_boundInventory.TryGetDevice(index, out ManagedDevice device)) return false;
            BindingRecord binding = _bindings[index];
            if (binding.Reserved != 0) return false;
            Candidate candidate = SelectBest(in device);
            if (binding.State == StateUnbound)
            {
                if (candidate.Found || binding.DriverId != 0 || binding.NameToken != 0 ||
                    binding.MatchType != 0 || binding.Specificity != 0 ||
                    binding.Priority != 0 || binding.RegistrationOrder != 0) return false;
                unbound++;
                continue;
            }
            if (binding.State != StateBound || !candidate.Found ||
                binding.DriverId != _drivers[candidate.DriverIndex].DriverId ||
                binding.MatchType != _rules[candidate.RuleIndex].Type ||
                binding.Specificity != candidate.Specificity ||
                binding.Priority != candidate.Priority ||
                binding.RegistrationOrder != candidate.RegistrationOrder)
            {
                return false;
            }
            bound++;
        }
        return bound == _boundDeviceCount && unbound == _unboundDeviceCount;
    }

    internal bool Destroy()
    {
        if (_destroyed || _bound) return false;
        if (_arena.Free(in _bindingStorage) != KernelArenaStatus.Ok ||
            _arena.Free(in _ruleStorage) != KernelArenaStatus.Ok ||
            _arena.Free(in _driverStorage) != KernelArenaStatus.Ok ||
            _arena.Destroy() != KernelArenaStatus.Ok) return false;
        _destroyed = true;
        return true;
    }

    internal static bool TryRunPrecedenceTests(IKernelMemoryProvider provider)
    {
        ManagedDriverRegistry? registry = Create(provider);
        if (registry == null) return false;
        bool passed = false;
        try
        {
            ManagedDriverMatchRule classRule = new(
                ManagedDriverMatchType.Class, classCode: 0x01);
            ManagedDriverMatchRule classSpecificRule = new(
                ManagedDriverMatchType.ClassSubclassProgrammingInterface,
                classCode: 0x01, subclass: 0x06, programmingInterface: 0x01);
            ManagedDriverMatchRule exactRule = new(
                ManagedDriverMatchType.ExactVendorDevice,
                vendorId: 0x8086, deviceId: 0x2922);
            if (!registry.TryRegister(new ManagedDriverDefinition(
                    0x201, 0x434C4153, 10, new[] { classRule })) ||
                !registry.TryRegister(new ManagedDriverDefinition(
                    0x202, 0x53504543, 1, new[] { classSpecificRule })) ||
                !registry.TryRegister(new ManagedDriverDefinition(
                    0x203, 0x45584143, 0, new[] { exactRule })) ||
                !registry.TryFreeze()) return false;

            GxManagedKernelDeviceV1 descriptor = new()
            {
                Size = GxManagedKernelDeviceV1.ExpectedSize,
                AbiVersion = 1,
                DeviceKind = GxManagedKernelDeviceV1.DeviceKindPci,
                Segment = 0,
                Bus = 0,
                Device = 1,
                Function = 0,
                VendorId = 0x8086,
                DeviceId = 0x2922,
                ClassCode = 0x01,
                Subclass = 0x06,
                ProgrammingInterface = 0x01,
                HeaderType = 0,
                ResourceStartIndex = 0,
                ResourceCount = 0,
                Reserved = 0
            };
            ManagedDevice device = new(in descriptor);
            Candidate winner = registry.SelectBest(in device);
            if (!winner.Found || winner.DriverIndex != 2 ||
                winner.Specificity != 4) return false;

            ManagedDriverRegistry? tieRegistry = Create(provider);
            if (tieRegistry == null) return false;
            bool tieDestroyed = false;
            try
            {
                ManagedDriverMatchRule tieRule = new(
                    ManagedDriverMatchType.Class, classCode: 0x02);
                if (!tieRegistry.TryRegister(new ManagedDriverDefinition(
                        0x301, 0x54494531, 4, new[] { tieRule })) ||
                    !tieRegistry.TryRegister(new ManagedDriverDefinition(
                        0x302, 0x54494532, 9, new[] { tieRule })) ||
                    !tieRegistry.TryRegister(new ManagedDriverDefinition(
                        0x303, 0x54494533, 9, new[] { tieRule })) ||
                    !tieRegistry.TryFreeze()) return false;
                descriptor.ClassCode = 0x02;
                descriptor.VendorId = 0x1234;
                descriptor.DeviceId = 0x5678;
                device = new ManagedDevice(in descriptor);
                winner = tieRegistry.SelectBest(in device);
                if (!winner.Found || winner.DriverIndex != 1) return false;
            }
            finally
            {
                tieDestroyed = tieRegistry.Destroy();
            }
            if (!tieDestroyed) return false;
            passed = true;
        }
        finally
        {
            if (!registry.IsDestroyed && !registry.Destroy()) passed = false;
        }
        return passed;
    }

    private Candidate SelectBest(in ManagedDevice device)
    {
        Candidate best = default;
        for (uint driverIndex = 0; driverIndex != _driverCount; ++driverIndex)
        {
            DriverRecord driver = _drivers[driverIndex];
            for (uint ruleOffset = 0; ruleOffset != driver.RuleCount; ++ruleOffset)
            {
                uint ruleIndex = driver.RuleStart + ruleOffset;
                RuleRecord rule = _rules[ruleIndex];
                if (!Matches(in rule, in device)) continue;
                uint specificity = Specificity(rule.Type);
                if (!best.Found || specificity > best.Specificity ||
                    specificity == best.Specificity &&
                    (driver.Priority > best.Priority ||
                     driver.Priority == best.Priority &&
                     driver.RegistrationOrder < best.RegistrationOrder))
                {
                    best.Found = true;
                    best.DriverIndex = driverIndex;
                    best.RuleIndex = ruleIndex;
                    best.Specificity = specificity;
                    best.Priority = driver.Priority;
                    best.RegistrationOrder = driver.RegistrationOrder;
                }
            }
        }
        return best;
    }

    private static bool Matches(in RuleRecord rule, in ManagedDevice device)
    {
        return rule.Type switch
        {
            (uint)ManagedDriverMatchType.ExactVendorDevice =>
                device.VendorId == rule.VendorId && device.DeviceId == rule.DeviceId,
            (uint)ManagedDriverMatchType.ClassSubclassProgrammingInterface =>
                device.ClassCode == rule.ClassCode &&
                device.Subclass == rule.Subclass &&
                device.ProgrammingInterface == rule.ProgrammingInterface,
            (uint)ManagedDriverMatchType.ClassSubclass =>
                device.ClassCode == rule.ClassCode && device.Subclass == rule.Subclass,
            (uint)ManagedDriverMatchType.Class => device.ClassCode == rule.ClassCode,
            _ => false
        };
    }

    private static uint Specificity(uint type)
    {
        return type switch
        {
            (uint)ManagedDriverMatchType.ExactVendorDevice => 4,
            (uint)ManagedDriverMatchType.ClassSubclassProgrammingInterface => 3,
            (uint)ManagedDriverMatchType.ClassSubclass => 2,
            (uint)ManagedDriverMatchType.Class => 1,
            _ => 0
        };
    }

    private static bool ValidateRule(in ManagedDriverMatchRule rule)
    {
        return rule.Type switch
        {
            ManagedDriverMatchType.ExactVendorDevice =>
                rule.VendorId != 0 && rule.VendorId != 0xFFFF &&
                rule.DeviceId != 0 && rule.ClassCode == 0 &&
                rule.Subclass == 0 && rule.ProgrammingInterface == 0,
            ManagedDriverMatchType.ClassSubclassProgrammingInterface =>
                rule.VendorId == 0 && rule.DeviceId == 0,
            ManagedDriverMatchType.ClassSubclass =>
                rule.VendorId == 0 && rule.DeviceId == 0 &&
                rule.ProgrammingInterface == 0,
            ManagedDriverMatchType.Class =>
                rule.VendorId == 0 && rule.DeviceId == 0 &&
                rule.Subclass == 0 && rule.ProgrammingInterface == 0,
            _ => false
        };
    }

    private static bool ValidateRule(in RuleRecord rule)
    {
        return rule.Reserved0 == 0 && rule.Reserved1 == 0 &&
            rule.Type switch
            {
                (uint)ManagedDriverMatchType.ExactVendorDevice =>
                    rule.VendorId != 0 && rule.VendorId != 0xFFFF &&
                    rule.DeviceId != 0 && rule.ClassCode == 0 &&
                    rule.Subclass == 0 && rule.ProgrammingInterface == 0,
                (uint)ManagedDriverMatchType.ClassSubclassProgrammingInterface =>
                    rule.VendorId == 0 && rule.DeviceId == 0,
                (uint)ManagedDriverMatchType.ClassSubclass =>
                    rule.VendorId == 0 && rule.DeviceId == 0 &&
                    rule.ProgrammingInterface == 0,
                (uint)ManagedDriverMatchType.Class =>
                    rule.VendorId == 0 && rule.DeviceId == 0 &&
                    rule.Subclass == 0 && rule.ProgrammingInterface == 0,
                _ => false
            };
    }
}
