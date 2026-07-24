// Read-only Ghidra probe for the exact PvZ 1.0.0.1051 audio entry points.
// @category PvZMod

import java.util.LinkedHashSet;
import java.util.Set;

import ghidra.app.decompiler.DecompInterface;
import ghidra.app.decompiler.DecompileResults;
import ghidra.app.script.GhidraScript;
import ghidra.program.model.address.Address;
import ghidra.program.model.listing.Function;
import ghidra.program.model.symbol.Reference;

public final class PvZAudioProbe extends GhidraScript {
    private DecompInterface decompiler;

    @Override
    protected void run() throws Exception {
        decompiler = new DecompInterface();
        decompiler.openProgram(currentProgram);
        try {
            inspect("GameApp::PlaySample", 0x004560c0L);
            inspect("SexyApp::PlaySample", 0x00554c20L);
            inspect("SexyApp::PlaySampleWithVolume", 0x00554c50L);
            inspect("Music state helper", 0x0045b750L);
        } finally {
            decompiler.dispose();
        }
    }

    private void inspect(String label, long absoluteAddress) throws Exception {
        Address address = toAddr(absoluteAddress);
        Function function = getFunctionContaining(address);
        println("\n===== " + label + " @ " + address + " =====");
        if (function == null) {
            disassemble(address);
            function = createFunction(address, null);
        }
        if (function == null) {
            println("Could not create a temporary function at this address.");
            return;
        }
        println("Function: " + function.getName() + " entry=" + function.getEntryPoint());
        DecompileResults result = decompiler.decompileFunction(function, 60, monitor);
        if (result.decompileCompleted() && result.getDecompiledFunction() != null) {
            println(result.getDecompiledFunction().getC());
        } else {
            println("Decompile failed: " + result.getErrorMessage());
        }

        Set<String> callers = new LinkedHashSet<>();
        for (Reference reference : getReferencesTo(function.getEntryPoint())) {
            Function caller = getFunctionContaining(reference.getFromAddress());
            if (caller != null) {
                callers.add(caller.getName() + " @ " + caller.getEntryPoint() +
                    " via " + reference.getFromAddress());
            }
        }
        println("Direct callers: " + callers.size());
        for (String caller : callers) println("  " + caller);
    }
}
