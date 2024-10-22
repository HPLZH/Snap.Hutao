﻿// Copyright (c) DGP Studio. All rights reserved.
// Licensed under the MIT license.

using Snap.Hutao.Model.Intrinsic;
using Snap.Hutao.Model.Metadata;
using Snap.Hutao.Model.Primitive;
using System.Collections.Immutable;

namespace Snap.Hutao.Service.Metadata.ContextAbstraction;

internal interface IMetadataDictionaryLevelWeaponGrowCurveSource
{
    ImmutableDictionary<Level, TypeValueCollection<GrowCurveType, float>> LevelDictionaryWeaponGrowCurveMap { get; set; }
}