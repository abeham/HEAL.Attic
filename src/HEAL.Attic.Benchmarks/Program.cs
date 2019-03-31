#region License Information
/*
 * This file is part of HEAL.Attic which is licensed under the MIT license.
 * See the LICENSE file in the project root for more information.
 */
#endregion

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Formatters.Binary;

namespace HEAL.Attic.Benchmarks {
  public class Program {
    public static readonly int REPS = 10;
    public static void Main(string[] args) {
      Console.WriteLine("Warmup (int[])");
      Benchmark(MakeIntArray);
      Console.WriteLine();

      Console.WriteLine("int[]");
      Benchmark(MakeIntArray);
      Console.WriteLine();

      Console.WriteLine("int[] with limited size:");
      Benchmark(MakeLimitedIntArray);
      Console.WriteLine();

      Console.WriteLine("double[]:");
      Benchmark(MakeDoubleArray);
      Console.WriteLine();

      Console.WriteLine("List<int>:");
      Benchmark(MakeIntList);
      Console.WriteLine();

      Console.WriteLine("List<int> with limited size:");
      Benchmark(MakeLimitedIntList);
      Console.WriteLine();

      Console.WriteLine("List<double>:");
      Benchmark(MakeDoubleList);
      Console.WriteLine();


      Console.WriteLine("ushort[]:");
      Benchmark(MakeUShortArray);
      Console.WriteLine();

      Console.WriteLine("int[,]:");
      Benchmark(MakeInt2Array);
      Console.WriteLine();

      Console.WriteLine("int[,,]:");
      Benchmark(MakeInt3Array);
      Console.WriteLine();

      Console.WriteLine("int[,,,]:");
      Benchmark(MakeInt4Array);
      Console.WriteLine();

      Console.WriteLine("Linked list:");
      Benchmark(MakeLinkedList);
      Console.WriteLine();

      Console.WriteLine("Random graph:");
      Benchmark(MakeGraph);
      Console.WriteLine();
    }

    public static object MakeIntArray(int size, Random rand) {
      var arr = new int[size];
      for (int i = 0; i < arr.Length; i++) {
        arr[i] = rand.Next();
      }
      return arr;
    }


    public static object MakeUShortArray(int size, Random rand) {
      var arr = new ushort[size];
      for (int i = 0; i < arr.Length; i++) {
        arr[i] = (ushort)rand.Next(ushort.MinValue, ushort.MaxValue);
      }
      return arr;
    }

    public static object MakeInt2Array(int size, Random rand) {
      var arr = new int[(int)Math.Sqrt(size), (int)Math.Sqrt(size)];
      for (int i = 0; i < arr.GetLength(0); i++) {
        for (int j = 0; j < arr.GetLength(1); j++) {
          arr[i, j] = rand.Next();
        }
      }
      return arr;
    }
    public static object MakeInt3Array(int size, Random rand) {
      var arr = new int[(int)Math.Pow(size, 1.0 / 3), (int)Math.Pow(size, 1.0 / 3), (int)Math.Pow(size, 1.0 / 3)];
      for (int i = 0; i < arr.GetLength(0); i++) {
        for (int j = 0; j < arr.GetLength(1); j++) {
          for (int k = 0; k < arr.GetLength(2); k++) {
            arr[i, j, k] = rand.Next();
          }
        }
      }
      return arr;
    }
    public static object MakeInt4Array(int size, Random rand) {
      var arr = new int[(int)Math.Pow(size, 1 / 4.0), (int)Math.Pow(size, 1 / 4.0), (int)Math.Pow(size, 1 / 4.0), (int)Math.Pow(size, 1 / 4.0)];
      for (int i = 0; i < arr.GetLength(0); i++) {
        for (int j = 0; j < arr.GetLength(1); j++) {
          for (int k = 0; k < arr.GetLength(2); k++) {
            for (int l = 0; l < arr.GetLength(3); l++) {
              arr[i, j, k, l] = rand.Next();
            }
          }
        }
      }
      return arr;
    }

    public static object MakeLimitedIntArray(int size, Random rand) {
      int maxInt = 20;
      var arr = new int[size];
      for (int i = 0; i < arr.Length; i++) {
        arr[i] = rand.Next(maxInt);
      }
      return arr;
    }

    public static object MakeDoubleArray(int size, Random rand) {
      var arr = new double[size];
      for (int i = 0; i < arr.Length; i++) {
        arr[i] = rand.NextDouble();
      }
      return arr;
    }

    public static object MakeIntList(int size, Random rand) {
      var l = new List<int>(size);
      for (int i = 0; i < size; i++) {
        l.Add(rand.Next());
      }
      return l;
    }

    public static object MakeLimitedIntList(int size, Random rand) {
      int maxInt = 20;
      var l = new List<int>(size);
      for (int i = 0; i < size; i++) {
        l.Add(rand.Next(maxInt));
      }
      return l;
    }

    public static object MakeDoubleList(int size, Random rand) {
      var l = new List<double>(size);
      for (int i = 0; i < size; i++) {
        l.Add(rand.NextDouble());
      }
      return l;
    }

    [StorableType("03DF0814-07A1-4DFE-9D8A-A8B328CD6B50")]
    [Serializable]
    class Node {
      [Storable]
      public List<Node> children;
      private Node(StorableConstructorFlag _) { }
      public Node() {
        children = new List<Node>();
      }
    }

    public static object MakeGraph(int size, Random rand) {
      int maxChildren = 20;
      var allNodes = new List<Node>();
      allNodes.Add(new Node()); // start with at least one node
      for (int i = 0; i < size; i++) {
        var node = new Node();
        var numChildren = rand.Next(maxChildren) + 1;
        for (int j = 0; j < numChildren; j++) {
          node.children.Add(allNodes[rand.Next(allNodes.Count)]);
        }
        allNodes.Add(node);
      }
      return allNodes;
    }

    [StorableType("B9C2AA20-A18C-4124-90B8-B181BF7691B4")]
    [Serializable]
    private class ListNode {
      [Storable]
      public ListNode Next;
    }
    public static object MakeLinkedList(int size, Random rand) {
      var first = new ListNode();
      var cur = first;
      for (int i = 0; i < size; i++) {
        cur.Next = new ListNode();
        cur = cur.Next;
      }
      return first;
    }


    private static void Benchmark(Func<int, Random, object> createObj) {
      var rand = new Random(1234);
      long fileSize = 0;
      Console.WriteLine("| Method | Elements | Serialization time (ms) | Deserialization time (ms) | File size (kB) | avg. bytes per element |");
      Console.WriteLine("|-------:|---------:|------------------------:|--------------------------:|---------------:|------------------------|");
      for (int e = 13; e <= 21; e++) {
        int numElems = 1 << e;

        object[] objs = Enumerable.Range(0, REPS).Select(x => createObj(1 << e, rand)).ToArray();

        long serializationTime = 0;
        long deserializationTime = 0;

        for (int reps = 0; reps < REPS; reps++) {
          (var buf, var time) = RunSerialize(objs[reps], SerializeWithAttic);
          serializationTime += time;
          fileSize = buf.Length;

          (_, time) = RunDeserialize(buf, DeserializeWithAttic);
          deserializationTime += time;
        }
        Console.WriteLine($"| Attic  | {numElems,8} | {serializationTime / (double)REPS,23:N1} | {deserializationTime / (double)REPS,25:N1} | {fileSize / 1024.0,14:N1} | {fileSize / (double)numElems,22:N1} | ");

        serializationTime = 0;
        deserializationTime = 0;

        for (int reps = 0; reps < REPS; reps++) {
          (var buf, var time) = RunSerialize(objs[reps], SerializeWithBinaryFormatter);
          serializationTime += time;
          fileSize = buf.Length;

          (_, time) = RunDeserialize(buf, DeserializeWithBinaryFormatter);
          deserializationTime += time;
        }
        Console.WriteLine($"| BinFor | {numElems,8} | {serializationTime / (double)REPS,23:N1} | {deserializationTime / (double)REPS,25:N1} | {fileSize / 1024.0,14:N1} | {fileSize / (double)numElems,22:N1} | ");
      }
    }

    private static (byte[], long) RunSerialize(object obj, Func<object, MemoryStream> serialize) {
      long fileSize;

      byte[] buf;
      var sw = Stopwatch.StartNew();
      using (var memStream = serialize(obj)) {
        buf = memStream.GetBuffer();
        fileSize = memStream.Position;
      }
      sw.Stop();

      var fixedBuf = new byte[fileSize];
      Array.Copy(buf, fixedBuf, fileSize);

      return (fixedBuf, sw.ElapsedMilliseconds);
    }

    private static (object, long) RunDeserialize(byte[] buf, Func<MemoryStream, object> deserialize) {
      object obj;

      var sw = Stopwatch.StartNew();
      using (var memStream = new MemoryStream(buf))
        obj = deserialize(memStream);
      sw.Stop();

      return (obj, sw.ElapsedMilliseconds);
    }

    private static MemoryStream SerializeWithAttic(object obj) {
      var memStream = new MemoryStream();
      new ProtoBufSerializer().Serialize(obj, memStream, false);
      return memStream;
    }

    private static object DeserializeWithAttic(MemoryStream stream) {
      return new ProtoBufSerializer().Deserialize(stream);
    }

    private static MemoryStream SerializeWithBinaryFormatter(object obj) {
      var memStream = new MemoryStream();
      new BinaryFormatter().Serialize(memStream, obj);
      return memStream;
    }

    private static object DeserializeWithBinaryFormatter(MemoryStream stream) {
      return new BinaryFormatter().Deserialize(stream);
    }
  }
}
