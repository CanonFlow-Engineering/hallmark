package org.canonflow.hallmark.receiptdrill

import android.app.Activity
import android.os.Bundle
import android.util.AtomicFile
import android.widget.TextView
import java.io.File
import java.io.FileNotFoundException
import org.json.JSONObject

private data class Receipt(val schema: Int, val artifactId: String)

private sealed interface Upgrade {
    data class Save(val receipt: Receipt) : Upgrade
    data class Refused(val reason: String) : Upgrade
}

private fun nextReceipt(prior: Receipt?, version: Int): Upgrade = when {
    prior == null && version == 1 -> Upgrade.Save(Receipt(1, "mobile-draft-0001"))
    prior == null -> Upgrade.Refused("Install the baseline first")
    prior.artifactId.isBlank() -> Upgrade.Refused("Missing artifact")
    prior.schema == version -> Upgrade.Save(prior)
    prior.schema == 1 && version == 2 -> Upgrade.Save(prior.copy(schema = 2))
    else -> Upgrade.Refused("Unsupported receipt schema")
}

class ReceiptActivity : Activity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        val state = AtomicFile(File(filesDir, "receipt.json"))
        val prior = try {
            val value = JSONObject(state.readFully().decodeToString())
            Receipt(value.getInt("schema"), value.getString("artifactId"))
        } catch (_: FileNotFoundException) {
            null
        }
        val display = when (val result = nextReceipt(prior, applicationVersion)) {
            is Upgrade.Refused -> result.reason
            is Upgrade.Save -> {
                val receipt = result.receipt
                val content = JSONObject()
                    .put("schema", receipt.schema)
                    .put("artifactId", receipt.artifactId)
                    .toString()
                val stream = state.startWrite()
                try {
                    stream.write(content.encodeToByteArray())
                    state.finishWrite(stream)
                } catch (error: java.io.IOException) {
                    state.failWrite(stream)
                    throw error
                }
                "Receipt ${receipt.artifactId} · schema ${receipt.schema}"
            }
        }
        setContentView(TextView(this).apply { text = display })
    }
}
