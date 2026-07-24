// Prints the generated sound-resource initializer used to recover SOUND_* globals.
// Read-only; exact for Plants vs. Zombies 1.0.0.1051.
// @category PvZMod

import ghidra.app.decompiler.DecompInterface;
import ghidra.app.decompiler.DecompileResults;
import ghidra.app.script.GhidraScript;
import ghidra.program.model.listing.Function;

public final class PvZSoundCatalogProbe extends GhidraScript {
    @Override
    protected void run() throws Exception {
        DecompInterface decompiler = new DecompInterface();
        decompiler.openProgram(currentProgram);
        try {
            long[] addresses = {0x004528E0L, 0x00474700L, 0x004776B0L, 0x0047D9A0L};
            for (long address : addresses) {
                Function function = getFunctionAt(toAddr(address));
                if (function == null) {
                    println("Function was not found at " + toAddr(address) + '.');
                    continue;
                }
                println("\n===== " + function.getName() + " @ " + function.getEntryPoint() + " =====");
                DecompileResults result = decompiler.decompileFunction(function, 180, monitor);
                if (result.decompileCompleted() && result.getDecompiledFunction() != null) {
                    println(result.getDecompiledFunction().getC());
                } else {
                    println("Decompile failed: " + result.getErrorMessage());
                }
            }
        } finally {
            decompiler.dispose();
        }
    }
}
