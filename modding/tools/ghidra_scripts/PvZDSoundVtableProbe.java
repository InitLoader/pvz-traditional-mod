// Recovers the exact PvZ 1.0.0.1051 DSoundManager vtable from MSVC RTTI.
// @category PvZMod

import java.nio.ByteBuffer;
import java.nio.ByteOrder;

import ghidra.app.decompiler.DecompInterface;
import ghidra.app.decompiler.DecompileResults;
import ghidra.app.script.GhidraScript;
import ghidra.program.model.address.Address;
import ghidra.program.model.listing.Function;

public final class PvZDSoundVtableProbe extends GhidraScript {
    private DecompInterface decompiler;

    @Override
    protected void run() throws Exception {
        decompiler = new DecompInterface();
        decompiler.openProgram(currentProgram);
        try {
            Address typeName = findAscii(".?AVDSoundManager@Sexy@@");
            println("RTTI name @ " + typeName);
            if (typeName == null) return;
            Address typeDescriptor = typeName.subtract(8);
            println("TypeDescriptor @ " + typeDescriptor);
            Address descriptorPointer = findPointer(typeDescriptor, currentProgram.getMinAddress());
            while (descriptorPointer != null) {
                Address locator = descriptorPointer.subtract(12);
                println("CompleteObjectLocator candidate @ " + locator);
                Address locatorPointer = findPointer(locator, currentProgram.getMinAddress());
                while (locatorPointer != null) {
                    Address vtable = locatorPointer.add(4);
                    println("\n===== DSoundManager vtable candidate @ " + vtable + " =====");
                    printSlots(vtable, 20);
                    locatorPointer = findPointer(locator, locatorPointer.next());
                }
                descriptorPointer = findPointer(typeDescriptor, descriptorPointer.next());
            }
        } finally {
            decompiler.dispose();
        }
    }

    private Address findAscii(String value) throws Exception {
        return currentProgram.getMemory().findBytes(
            currentProgram.getMinAddress(), value.getBytes("US-ASCII"), null, true, monitor);
    }

    private Address findPointer(Address target, Address start) throws Exception {
        byte[] bytes = ByteBuffer.allocate(4).order(ByteOrder.LITTLE_ENDIAN)
            .putInt((int)target.getOffset()).array();
        return currentProgram.getMemory().findBytes(start, bytes, null, true, monitor);
    }

    private void printSlots(Address vtable, int count) throws Exception {
        for (int slot = 0; slot < count; ++slot) {
            Address entry = vtable.add(slot * 4L);
            long targetValue = Integer.toUnsignedLong(currentProgram.getMemory().getInt(entry));
            Address target = toAddr(targetValue);
            Function function = getFunctionAt(target);
            println(String.format("slot +0x%02X -> %s %s", slot * 4, target,
                function == null ? "" : function.getName()));
            if (slot == 3 || slot == 8 || slot == 17 || slot == 18) {
                decompile(target);
            }
        }
    }

    private void decompile(Address address) throws Exception {
        Function function = getFunctionAt(address);
        if (function == null) {
            println("No function at " + address);
            return;
        }
        DecompileResults result = decompiler.decompileFunction(function, 60, monitor);
        if (result.decompileCompleted() && result.getDecompiledFunction() != null) {
            println(result.getDecompiledFunction().getC());
        } else {
            println("Decompile failed at " + address + ": " + result.getErrorMessage());
        }
    }
}
