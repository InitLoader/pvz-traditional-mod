// Focused read-only probe for PvZ resource/sound lookup functions.
// @category PvZMod

import java.nio.charset.StandardCharsets;
import java.util.LinkedHashSet;
import java.util.Set;

import ghidra.app.decompiler.DecompInterface;
import ghidra.app.decompiler.DecompileResults;
import ghidra.app.script.GhidraScript;
import ghidra.program.model.address.Address;
import ghidra.program.model.listing.Function;
import ghidra.program.model.symbol.Reference;

public final class PvZAudioResourceProbe extends GhidraScript {
    private DecompInterface decompiler;

    @Override
    protected void run() throws Exception {
        decompiler = new DecompInterface();
        decompiler.openProgram(currentProgram);
        try {
            inspectAddress("TodLoadResources candidate", 0x00513120L);
            inspectString("LoadingSounds", true);
            inspectString("SOUND_CHOMP", false);
            inspectString("sounds\\tap.ogg", true);
            inspectString("resources.xml", true);
        } finally {
            decompiler.dispose();
        }
    }

    private void inspectAddress(String label, long value) throws Exception {
        Address address = toAddr(value);
        Function function = getFunctionContaining(address);
        println("\n===== " + label + " @ " + address + " =====");
        if (function == null) {
            println("No function.");
            return;
        }
        decompile(function);
    }

    private void inspectString(String needle, boolean decompileCallers) throws Exception {
        byte[] bytes = needle.getBytes(StandardCharsets.US_ASCII);
        Address found = currentProgram.getMemory().findBytes(
            currentProgram.getMinAddress(), bytes, null, true, monitor);
        println("\n===== STRING " + needle + " @ " + found + " =====");
        if (found == null) return;
        Set<Address> seen = new LinkedHashSet<>();
        for (Reference reference : getReferencesTo(found)) {
            Function function = getFunctionContaining(reference.getFromAddress());
            println("ref " + reference.getFromAddress() + " function=" +
                (function == null ? "none" : function.getName() + " @ " + function.getEntryPoint()));
            if (decompileCallers && function != null && seen.add(function.getEntryPoint())) {
                decompile(function);
            }
        }
    }

    private void decompile(Function function) throws Exception {
        println("\n--- " + function.getName() + " @ " + function.getEntryPoint() + " ---");
        DecompileResults result = decompiler.decompileFunction(function, 60, monitor);
        if (result.decompileCompleted() && result.getDecompiledFunction() != null) {
            println(result.getDecompiledFunction().getC());
        } else {
            println("Decompile failed: " + result.getErrorMessage());
        }
    }
}
