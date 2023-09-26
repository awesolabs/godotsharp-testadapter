using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public class MyTests {
	[TestMethod]
	public void MyTestMethod() {
		var aresource = new Sprite3D();
		Assert.IsNotNull(value: aresource);
		Assert.IsTrue(condition: 2 + 2 == 4);
	}
}