using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

// Reads the client-side providers' assembly rather than running it: what the tracker and the queue
// declare, what a queue item is, and the IL of the methods that start the queue, wait in it and
// feed it. This is how the absence of a way to stop the queue was established.
public static class ProvidersReflection
{
    private const BindingFlags Declared =
        BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    public static void Members()
    {
        var assembly = Assembly.Load(Program.ProvidersName);
        foreach (var name in new[] { "MS.Internal.AutomationProxies.WinEventTracker", "MS.Internal.AutomationProxies.QueueProcessor" })
        {
            var type = assembly.GetType(name);
            Console.WriteLine("== " + name + (type == null ? " MISSING" : ""));
            if (type == null)
            {
                continue;
            }

            foreach (var member in type.GetMembers(Declared))
            {
                Console.WriteLine("  " + member.MemberType + " " + member);
            }
        }
    }

    public static void Items()
    {
        var assembly = Assembly.Load(Program.ProvidersName);
        var baseType = assembly.GetType("MS.Internal.AutomationProxies.QueueItem");
        Console.WriteLine("QueueItem: " + baseType);
        foreach (var member in baseType.GetMembers(Declared))
        {
            Console.WriteLine("  " + member.MemberType + " " + member);
        }

        foreach (var type in assembly.GetTypes().Where(t => baseType.IsAssignableFrom(t) && t != baseType))
        {
            Console.WriteLine("== " + type.FullName);
            foreach (var member in type.GetMembers(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                Console.WriteLine("  " + member.MemberType + " " + member);
            }
        }
    }

    public static void Il()
    {
        var assembly = Assembly.Load(Program.ProvidersName);
        var codes = new Dictionary<short, OpCode>();
        foreach (var field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            var code = (OpCode)field.GetValue(null);
            codes[code.Value] = code;
        }

        var tracker = assembly.GetType("MS.Internal.AutomationProxies.WinEventTracker");
        var startStop = tracker.GetNestedType("StartStopDelegate", BindingFlags.NonPublic);
        Console.WriteLine("StartStopDelegate.Invoke: " + startStop.GetMethod("Invoke"));

        var wanted = new[]
        {
            Tuple.Create("MS.Internal.AutomationProxies.QueueProcessor", "PostSyncWorkItem"),
            Tuple.Create("MS.Internal.AutomationProxies.QueueProcessor", "WaitForWork"),
            Tuple.Create("MS.Internal.AutomationProxies.QueueProcessor", "StartOnThread"),
            Tuple.Create("MS.Internal.AutomationProxies.QueueItem+MSAAWinEventItem", "Process"),
            Tuple.Create("MS.Internal.AutomationProxies.QueueItem+SyncQueueItem", "Process"),
            Tuple.Create("MS.Internal.AutomationProxies.WinEventTracker", "GetCallbackQueue"),
            Tuple.Create("MS.Internal.AutomationProxies.WinEventTracker", ".cctor"),
        };

        foreach (var pair in wanted)
        {
            var type = assembly.GetType(pair.Item1);
            var methods = type.GetMethods(Declared)
                .Cast<MethodBase>()
                .Concat(type.GetConstructors(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                .Where(m => m.Name == pair.Item2);
            foreach (var method in methods)
            {
                Console.WriteLine("== " + pair.Item1 + "::" + method);
                Dump(method, codes);
            }
        }
    }

    private static void Dump(MethodBase method, Dictionary<short, OpCode> codes)
    {
        var body = method.GetMethodBody();
        if (body == null)
        {
            Console.WriteLine("  (no body)");
            return;
        }

        var il = body.GetILAsByteArray();
        var i = 0;
        while (i < il.Length)
        {
            var at = i;
            short value = il[i];
            if (il[i] == 0xFE)
            {
                value = (short)(0xFE00 | il[i + 1]);
                i += 2;
            }
            else
            {
                i += 1;
            }

            OpCode code;
            if (!codes.TryGetValue(value, out code))
            {
                Console.WriteLine("  ?? " + value);
                break;
            }

            var operand = "";
            int size;
            switch (code.OperandType)
            {
                case OperandType.InlineNone:
                    size = 0;
                    break;
                case OperandType.ShortInlineBrTarget:
                    operand = "-> " + (i + 1 + (sbyte)il[i]);
                    size = 1;
                    break;
                case OperandType.ShortInlineI:
                case OperandType.ShortInlineVar:
                    operand = il[i].ToString();
                    size = 1;
                    break;
                case OperandType.InlineVar:
                    size = 2;
                    break;
                case OperandType.InlineI8:
                case OperandType.InlineR:
                    size = 8;
                    break;
                case OperandType.InlineBrTarget:
                    operand = "-> " + (i + 4 + BitConverter.ToInt32(il, i));
                    size = 4;
                    break;
                case OperandType.InlineSwitch:
                    size = 4 + 4 * BitConverter.ToInt32(il, i);
                    break;
                default:
                    size = 4;
                    break;
            }

            if (code.OperandType == OperandType.InlineMethod || code.OperandType == OperandType.InlineField ||
                code.OperandType == OperandType.InlineType || code.OperandType == OperandType.InlineTok)
            {
                try
                {
                    var member = method.Module.ResolveMember(BitConverter.ToInt32(il, i),
                        method.DeclaringType.IsGenericType ? method.DeclaringType.GetGenericArguments() : null, null);
                    operand = member.DeclaringType == null ? member.ToString() : member.DeclaringType.Name + "::" + member;
                }
                catch (Exception error)
                {
                    operand = "?" + error.GetType().Name;
                }
            }
            else if (code.OperandType == OperandType.InlineI)
            {
                operand = BitConverter.ToInt32(il, i).ToString();
            }

            Console.WriteLine("  " + at.ToString().PadLeft(4) + " " + code.Name + " " + operand);
            i += size;
        }

        foreach (var clause in body.ExceptionHandlingClauses)
        {
            Console.WriteLine("  clause " + clause.Flags + " try " + clause.TryOffset + "+" + clause.TryLength +
                              " handler " + clause.HandlerOffset + "+" + clause.HandlerLength);
        }
    }
}
