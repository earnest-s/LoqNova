// Quick verification of progression mapping
using System;

class VerifyMapping {
    static void Main() {
        var tests = new (double progression, double z1, double z2, double z3, double z4)[]
        {
            (0.0, 0, 0, 0, 0),
            (0.25, 0.25, 0, 0, 0),
            (0.99, 0.99, 0, 0, 0),
            (1.0, 1, 0, 0, 0),
            (1.25, 1, 0.25, 0, 0),
            (1.99, 1, 0.99, 0, 0),
            (2.0, 1, 1, 0, 0),
            (2.50, 1, 1, 0.50, 0),
            (3.0, 1, 1, 1, 0),
            (3.50, 1, 1, 1, 0.50),
            (4.0, 1, 1, 1, 1),
        };

        bool allPass = true;
        foreach (var (p, ez1, ez2, ez3, ez4) in tests) {
            double z1 = Math.Clamp(p, 0, 1);
            double z2 = Math.Clamp(p - 1, 0, 1);
            double z3 = Math.Clamp(p - 2, 0, 1);
            double z4 = Math.Clamp(p - 3, 0, 1);
            
            bool pass = Math.Abs(z1 - ez1) < 0.001 && 
                        Math.Abs(z2 - ez2) < 0.001 && 
                        Math.Abs(z3 - ez3) < 0.001 && 
                        Math.Abs(z4 - ez4) < 0.001;
            
            Console.WriteLine($"p={p:F2} -> Z1={z1:F2} Z2={z2:F2} Z3={z3:F2} Z4={z4:F2} {(pass ? "PASS" : "FAIL")}");
            if (!pass) allPass = false;
        }
        
        // Verify invariant Z1 >= Z2 >= Z3 >= Z4 for all p in [0, 4]
        Console.WriteLine("\nInvariant check:");
        for (double p = 0; p <= 4; p += 0.1) {
            double z1 = Math.Clamp(p, 0, 1);
            double z2 = Math.Clamp(p - 1, 0, 1);
            double z3 = Math.Clamp(p - 2, 0, 1);
            double z4 = Math.Clamp(p - 3, 0, 1);
            if (!(z1 >= z2 && z2 >= z3 && z3 >= z4)) {
                Console.WriteLine($"FAIL at p={p}: Z1={z1} Z2={z2} Z3={z3} Z4={z4}");
                allPass = false;
            }
        }
        Console.WriteLine(allPass ? "ALL TESTS PASSED" : "SOME TESTS FAILED");
    }
}