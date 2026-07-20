using System;
using Avalonia.Input.Platform;

class Test2 {
    static void Print() {
        foreach (var m in typeof(IClipboard).GetMethods()) {
            Console.WriteLine(m.Name);
        }
    }
}
