﻿#region License Information
/*
 * This file is part of HEAL.Attic which is licensed under the MIT license.
 * See the LICENSE file in the project root for more information.
 */
#endregion

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using Google.Protobuf;

namespace HEAL.Attic {
  public sealed class Mapper {
    internal class MappingEqualityComparer : IEqualityComparer<object> {
      bool IEqualityComparer<object>.Equals(object x, object y) {
        if (x == y) return true;

        // for ValueTypes and strings also check Equals
        if (x is ValueType xVal && y is ValueType yVal) return xVal.Equals(yVal);
        if (x is string xStr && y is string yStr) return xStr.Equals(yStr);

        return false;
      }

      int IEqualityComparer<object>.GetHashCode(object obj) {
        return obj == null ? 0 : obj.GetHashCode() ^ obj.GetType().GetHashCode();
      }
    }

    private static StaticCache staticCache;
    private static object locker = new object();
    public static StaticCache StaticCache {
      get {
        lock (locker) {
          if (staticCache == null) staticCache = new StaticCache();
          return staticCache;
        }
      }
    }

    private Index<ITransformer> transformers;
    private Index<Type> types;
    private Index<string> strings;
    private Dictionary<uint, Box> boxId2Box;
    private Index<StorableTypeLayout> storableTypeLayouts;
    private Index<TypeMetadata> typeMetadata;
    private Index<ArrayMetadata> arrayMetadata;

    private readonly Queue<Tuple<object, Box>> objectsToProcess = new Queue<Tuple<object, Box>>();

    private readonly Dictionary<object, uint> object2BoxId;
    private readonly Dictionary<uint, object> boxId2Object;
    private readonly Dictionary<string, Dictionary<string, string>> componentInfoKeys; // cache for strings <TypeGUID>.<MemberName> for accessing fields and properties of StorableTypes
    private readonly Dictionary<string, StorableTypeLayout> type2layout; // GUID -> Layout
    private readonly Dictionary<Type, uint> type2TypeMetadataId;

    public CancellationToken CancellationToken { get; private set; }

    public uint BoxCount { get; private set; }

    public Mapper() {
      transformers = new Index<ITransformer>();
      types = new Index<Type>();

      boxId2Box = new Dictionary<uint, Box>();
      object2BoxId = new Dictionary<object, uint>(new MappingEqualityComparer());
      boxId2Object = new Dictionary<uint, object>();
      strings = new Index<string>();
      componentInfoKeys = new Dictionary<string, Dictionary<string, string>>();
      type2layout = new Dictionary<string, StorableTypeLayout>();

      typeMetadata = new Index<TypeMetadata>();
      type2TypeMetadataId = new Dictionary<Type, uint>();

      arrayMetadata = new Index<ArrayMetadata>();

      GetTypeId(typeof(Type).GetType());

      storableTypeLayouts = new Index<StorableTypeLayout>();

      BoxCount = 0;
    }

    #region Transformers
    public uint GetTransformerId(ITransformer transformer) {
      return transformers.GetIndex(transformer);
    }

    public ITransformer GetTransformer(uint transformerId) {
      return transformers.GetValue(transformerId);
    }
    #endregion

    #region Types
    public uint GetTypeId(Type type) {
      return types.GetIndex(type);
    }

    public Type GetType(uint typeId) {
      return types.GetValue(typeId);
    }

    public bool TryGetType(uint typeId, out Type type) {
      return types.TryGetValue(typeId, out type);
    }
    #endregion

    #region StorableType layouts
    internal uint GetStorableTypeMetadata(string typeGuid) {
      if (!type2layout.TryGetValue(typeGuid, out StorableTypeLayout layout)) {
        layout = new StorableTypeLayout();
        layout.TypeGuid = typeGuid.ToString().ToUpperInvariant();
        type2layout.Add(typeGuid, layout);
        return storableTypeLayouts.GetIndex(layout); // add to index for storage
      }
      return storableTypeLayouts.GetIndex(layout);
    }

    internal uint GetStorableTypeLayoutId(StorableTypeLayout layout) {
      return storableTypeLayouts.GetIndex(layout);
    }

    internal StorableTypeLayout GetStorableTypeLayout(uint layoutId) {
      return storableTypeLayouts.GetValue(layoutId);
    }

    private static StorableTypeLayout StorableTypeMetadataToLayout(StorableTypeMetadata box, Mapper mapper) {
      var layout = new StorableTypeLayout();
      layout.TypeGuid = mapper.GetString(box.TypeGuid);
      layout.MemberNames = box.Names.Select(sId => mapper.GetString(sId)).ToList();
      layout.ParentLayoutId = box.Parent;
      layout.IsPopulated = true;
      return layout;
    }

    private static StorableTypeMetadata StorableTypeLayoutToMetadata(StorableTypeLayout layout, Mapper mapper) {
      var metadata = new StorableTypeMetadata();
      metadata.Names.AddRange(layout.MemberNames.Select(name => mapper.GetStringId(name)));
      metadata.TypeGuid = mapper.GetStringId(layout.TypeGuid);
      metadata.Parent = layout.ParentLayoutId;
      return metadata;
    }
    #endregion

    #region TypeMetadata
    internal uint GetTypeMetadataId(Type type, ITransformer transformer) {
      if (type2TypeMetadataId.TryGetValue(type, out uint id)) {
        var typeMsg = typeMetadata.GetValue(id);
        if (typeMsg.TransformerId == 0) typeMsg.TransformerId = GetTransformerId(transformer); // GetTransformerId returns 0 when transformer is null
        return id;
      } else {
        var typeMessage = new TypeMetadata();
        typeMessage.TransformerId = GetTransformerId(transformer);
        if (type.IsGenericType) {
          typeMessage.TypeId = GetTypeId(type.GetGenericTypeDefinition());
          typeMessage.GenericTypeMetadataIds.AddRange(type.GetGenericArguments().Select(t => GetTypeMetadataId(t, null)));
        } else if (type.IsArray) {
          typeMessage.TypeId = GetTypeId(typeof(Array));
          typeMessage.GenericTypeMetadataIds.Add(GetTypeMetadataId(type.GetElementType(), null));
        } else {
          typeMessage.TypeId = GetTypeId(type);
        }
        id = typeMetadata.GetIndex(typeMessage);
        type2TypeMetadataId.Add(type, id);
        return id;
      }
    }

    internal Type StorableTypeMetadataToType(TypeMetadata typeMetadata) {
      TryGetType(typeMetadata.TypeId, out Type type);
      if (type == null) return null;
      else {
        if (type.IsGenericType) {
          var genericArgumentTypes = typeMetadata.GenericTypeMetadataIds.Select(x => StorableTypeMetadataToType(GetTypeMetadata(x))).ToArray();
          return genericArgumentTypes.Any(x => x == null) ? null : type.MakeGenericType(genericArgumentTypes);
        } else if (type == typeof(Array)) {
          var arrayType = (Type)StorableTypeMetadataToType(GetTypeMetadata(typeMetadata.GenericTypeMetadataIds[0]));
          return arrayType?.MakeArrayType();
        } else {
          return type;
        }
      }
    }
    internal TypeMetadata GetTypeMetadata(uint typeMetadataId) {
      return typeMetadata.GetValue(typeMetadataId);
    }
    #endregion

    #region Boxes
    public uint GetBoxId(object o) {
      uint boxId;

      if (o == null)
        boxId = 0;
      else {
        if (object2BoxId.TryGetValue(o, out boxId)) return boxId;
        var type = o.GetType();
        var typeInfo = StaticCache.GetTypeInfo(type);
        if (typeInfo.Transformer == null) throw new ArgumentException("Cannot serialize object of type " + o.GetType());
        boxId = ++BoxCount;
        typeInfo.Used++;
        object2BoxId.Add(o, boxId);
        var box = typeInfo.Transformer.CreateBox(o, this);
        boxId2Box.Add(boxId, box);
        objectsToProcess.Enqueue(Tuple.Create(o, box));
      }
      return boxId;
    }

    public Box GetBox(uint boxId) {
      return boxId2Box[boxId];
    }

    public object GetObject(uint boxId) {
      object o;
      if (boxId2Object.TryGetValue(boxId, out o)) return o;

      boxId2Box.TryGetValue(boxId, out Box box);

      if (box == null)
        o = null;
      else {
        // to find the transformer we first need to find the type and then we get the corresponding transformer
        var typeMetadata = GetTypeMetadata(box.TypeMetadataId);
        var transformer = GetTransformer(typeMetadata.TransformerId);

        o = transformer.ToObject(box, this);
        boxId2Object.Add(boxId, o);
      }

      return o;
    }
    #endregion

    #region Strings
    public uint GetStringId(string str) {
      return strings.GetIndex(str);
    }
    public string GetString(uint stringId) {
      return strings.GetValue(stringId);
    }
    public string GetComponentInfoKey(string typeGuid, string memberName) {
      if (!componentInfoKeys.TryGetValue(typeGuid, out Dictionary<string, string> dict)) {
        dict = new Dictionary<string, string>();
        componentInfoKeys.Add(typeGuid, dict);
      }
      if (!dict.TryGetValue(memberName, out string componentInfoKey)) {
        componentInfoKey = typeGuid + "." + memberName;
        dict.Add(memberName, componentInfoKey);
      }
      return componentInfoKey;
    }
    #endregion

    #region ArrayMetadata
    internal uint GetArrayMetadataId(ArrayMetadata arrayInfo) {
      return arrayMetadata.GetIndex(arrayInfo);
    }
    internal ArrayMetadata GetArrayMetadata(uint id) {
      return arrayMetadata.GetValue(id);
    }
    #endregion

    public object CreateInstance(Type type) {
      try {
        return StaticCache.GetTypeInfo(type).GetConstructor()();
      } catch (Exception e) {
        throw new PersistenceException("Deserialization failed.", e);
      }
    }

    public static Bundle ToBundle(object root, out SerializationInfo info, CancellationToken cancellationToken = default(CancellationToken)) {
      StaticCache.UpdateRegisteredTypes();

      var mapper = new Mapper { CancellationToken = cancellationToken };
      var bundle = new Bundle();

      info = new SerializationInfo();

      var sw = new Stopwatch();
      sw.Start();

      bundle.RootBoxId = mapper.GetBoxId(root);

      while (mapper.objectsToProcess.Any()) {
        if (cancellationToken.IsCancellationRequested) return bundle;
        var tuple = mapper.objectsToProcess.Dequeue();
        var o = tuple.Item1;
        var box = tuple.Item2;
        var transformer = mapper.GetTransformer(mapper.GetTypeMetadata(box.TypeMetadataId).TransformerId);
        transformer.FillBox(box, o, mapper);
      }

      bundle.TransformerGuids.AddRange(mapper.transformers.GetValues().Select(x => x.Guid).Select(x => ByteString.CopyFrom(x.ToByteArray())));
      bundle.TypeGuids.AddRange(mapper.types.GetValues().Select(x => ByteString.CopyFrom(StaticCache.GetGuid(x).ToByteArray())));
      bundle.StorableTypeMetadata.AddRange(mapper.storableTypeLayouts.GetValues().Select(l => StorableTypeLayoutToMetadata(l, mapper)));
      bundle.ArrayMetadata.AddRange(mapper.arrayMetadata.GetValues());
      bundle.TypeMetadata.AddRange(mapper.typeMetadata.GetValues());
      bundle.Boxes.AddRange(mapper.boxId2Box.OrderBy(x => x.Key).Select(x => x.Value));
      bundle.Strings.AddRange(mapper.strings.GetValues());

      sw.Stop();

      info.Duration = sw.Elapsed;
      info.NumberOfSerializedObjects = mapper.object2BoxId.Keys.Count;
      info.SerializedTypes = mapper.types.GetValues();

      return bundle;
    }

    public static object ToObject(Bundle bundle, out SerializationInfo info, CancellationToken cancellationToken = default(CancellationToken)) {
      StaticCache.UpdateRegisteredTypes();

      var mapper = new Mapper { CancellationToken = cancellationToken };
      info = new SerializationInfo();

      var sw = new Stopwatch();
      sw.Start();

      mapper.transformers = new Index<ITransformer>(bundle.TransformerGuids.Select(x => new Guid(x.ToByteArray())).Select(StaticCache.GetTransformer));

      var types = new List<Type>();
      var unknownTypeGuids = new List<Guid>();
      for (int i = 0; i < bundle.TypeGuids.Count; i++) {
        var x = bundle.TypeGuids[i];
        var guid = new Guid(x.ToByteArray());
        if (StaticCache.TryGetType(guid, out Type type)) {
          types.Add(type);
        } else {
          unknownTypeGuids.Add(guid);
          types.Add(null);
        }
      }

      mapper.types = new Index<Type>(types);
      mapper.typeMetadata = new Index<TypeMetadata>(bundle.TypeMetadata);
      mapper.boxId2Box = bundle.Boxes.Select((b, i) => new { Box = b, Index = i }).ToDictionary(k => (uint)k.Index + 1, v => v.Box);
      mapper.strings = new Index<string>(bundle.Strings);
      mapper.storableTypeLayouts = new Index<StorableTypeLayout>(bundle.StorableTypeMetadata.Select(l => StorableTypeMetadataToLayout(l, mapper)));
      mapper.arrayMetadata = new Index<ArrayMetadata>(bundle.ArrayMetadata);

      var boxes = bundle.Boxes;

      for (int i = boxes.Count - 1; i >= 0; i--) {
        if (cancellationToken.IsCancellationRequested) return null;
        mapper.GetObject((uint)i + 1);
      }

      for (int i = boxes.Count; i > 0; i--) {
        if (cancellationToken.IsCancellationRequested) return null;
        var box = mapper.boxId2Box[(uint)i];
        var o = mapper.boxId2Object[(uint)i];
        if (o == null) continue;
        var transformer = mapper.GetTransformer(mapper.GetTypeMetadata(box.TypeMetadataId).TransformerId);
        transformer.FillFromBox(o, box, mapper);
      }

      var root = mapper.GetObject(bundle.RootBoxId);

      ExecuteAfterDeserializationHooks(mapper.boxId2Object.Values.GetEnumerator());

      sw.Stop();

      info.Duration = sw.Elapsed;
      info.NumberOfSerializedObjects = mapper.boxId2Object.Values.Count;
      info.SerializedTypes = types;
      info.UnknownTypeGuids = unknownTypeGuids;

      return root;
    }

    private static void ExecuteAfterDeserializationHooks(IEnumerator<object> e) {
      var emptyArgs = new object[0];

      while (e.MoveNext()) {
        var obj = e.Current;

        if (obj == null || !StorableTypeAttribute.IsStorableType(obj.GetType())) continue;

        var typeList = new LinkedList<Type>();
        for (var type = obj.GetType(); type != null; type = type.BaseType) {
          typeList.AddFirst(type);
        }

        foreach (var type in typeList) {
          if (!StorableTypeAttribute.IsStorableType(type)) continue;

          var typeInfo = StaticCache.GetTypeInfo(type);
          foreach (var hook in typeInfo.AfterDeserializationHooks) {
            try {
              hook.Invoke(obj, emptyArgs);
            } catch (TargetInvocationException t) {
              throw t.InnerException;
            }
          }
        }
      }
    }
  }
}
