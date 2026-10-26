# Set up card scanning

Everything needed for setup is included in the Windows download. No SDK, source checkout, command shell, or manually edited JSON is required.

1. Open **Edit deck**, select your character, enter your actual game version, and save your current deck. The bundled catalog supports **0.107.1**. A leading `v` is accepted; other patches remain unsupported.
2. In the game, reach an unhovered three-card reward and leave the game focused for a few seconds. If Scout is paused after manual entry, select **Resume scan** first.
3. Return to Scout within 30 seconds. Expand **Scan setup** and select **Set up scan**.
4. On the captured image, draw a tight box around the reward heading. Then draw the left, middle, and right card names in order. Include each complete name and any trailing `+`; exclude cost badges and artwork. You can select a step again to redraw its box.
5. Select **Save scan setup**, then focus the game. Three matching frames are needed before advice appears.

The image is used in memory; setup saves only the small heading template and region coordinates under `%LOCALAPPDATA%\Sts2Scout\calibration.json`. Full images are saved only when you enable **Allow saving diagnostic images** and explicitly press **Save image**.

Test multiple rewards and non-reward screens. If names stay unread, use **Enter cards** for manual advice and adjust the regions in **Set up scan**. Screen match is separate from card-name recognition and is not a probability. The setup image must actually show a reward; selecting a combat heading will calibrate the wrong screen.

Existing 16:9 profiles without OCR regions receive the default name regions automatically. The `v` prefix is normalized, and old five-card reward templates are removed. Other aspect ratios need the visual setup above. Different patches are not silently relabeled. Recalibrate after resolution/aspect, UI scale, or layout changes.

Merchant calibration remains a development-only legacy feature and is outside the card-reward MVP. No game screenshots or player calibration are bundled in the executable.
