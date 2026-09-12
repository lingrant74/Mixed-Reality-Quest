from pathlib import Path
p=Path('outputs/backend/test_api.py');s=p.read_text()
s=s.replace('ModelOutput(guidance="Please show the physical blocks.", highlight_piece_ids=[], evidence="insufficient")','ModelOutput(scene="unclear", bottom=None, middle=None, top=None)')
s=s.replace('ModelOutput(guidance="Hint", highlight_piece_ids=[], evidence="insufficient")','ModelOutput(scene="unclear", bottom=None, middle=None, top=None)')
s=s.replace('"audio_url", "mock"}', '"audio_url", "mock", "status", "advance_step"}')
s=s.replace('self.assertTrue(data["guidance"].startswith("Uncertain:"))','self.assertEqual(data["status"], "uncertain")\n        self.assertFalse(data["advance_step"])')
s=s.replace('state["current_step"]["id"], "block-1"','state["current_step"]["required_layers"], ["bottom"]')
s=s.replace(', {"placed_piece_ids": ["unknown"]}', '')
p.write_text(s)
p=Path('outputs/backend/test_vision.py');s=p.read_text()
a=s.index('    def test_uncertainty_is_explicit');b=s.index('    def test_local_only_configuration',a)
s=s[:a]+'''    def test_uncertainty_is_explicit(self):
        for scene in ("unclear", "rendered_only"):
            out = vision.ModelOutput(scene=scene, bottom=None, middle=None, top=None)
            status, text = vision.evaluate(out, 2)
            self.assertEqual(status, "uncertain")
            self.assertIn("physical cups", text)

''' + s[b:]
s=s.replace('self.assertEqual(payload["messages"][1]["images"], ["test-image"])','self.assertEqual(payload["messages"][1]["images"][1], "test-image")\n            self.assertTrue(payload["messages"][1]["images"][0])')
s=s.replace('"guidance": "Use the red block.", "highlight_piece_ids": ["block-1"], "evidence": "physical_visible"','"scene": "physical", "bottom": None, "middle": None, "top": None')
s=s.replace('self.assertEqual(out.highlight_piece_ids, ["block-1"])','self.assertEqual(out.scene, "physical")')
p.write_text(s)
