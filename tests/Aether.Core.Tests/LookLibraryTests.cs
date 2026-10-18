using Aether.Core.Playback.Video;
using Xunit;

namespace Aether.Core.Tests;

public sealed class LookLibraryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "aether-looks-" + Guid.NewGuid().ToString("N"));
    private readonly string _builtIn;
    private readonly string _user;
    private readonly LookLibrary _library;

    public LookLibraryTests()
    {
        _builtIn = Path.Combine(_root, "built-in");
        _user = Path.Combine(_root, "user");
        Directory.CreateDirectory(_builtIn);
        _library = new LookLibrary(_builtIn, _user);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string WriteCube(string directory, string name, string header = "LUT_3D_SIZE 33")
    {
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, name);
        File.WriteAllText(path, $"TITLE \"x\"\n# comment\n{header}\n0 0 0\n");
        return path;
    }

    [Fact]
    public void A_Missing_Looks_Folder_Lists_Nothing()
    {
        Assert.Empty(_library.UserGrades());
    }

    [Fact]
    public void User_Looks_Are_Listed_By_File_Name_A_To_Z_Skipping_Other_Files()
    {
        WriteCube(_user, "zebra.cube");
        WriteCube(_user, "Apple.cube");
        WriteCube(_user, "mango.cube");
        File.WriteAllText(Path.Combine(_user, "notes.txt"), "x");

        Assert.Equal(["user:Apple.cube", "user:mango.cube", "user:zebra.cube"], _library.UserGrades());
        Assert.Equal("Apple", LookLibrary.DisplayName("user:Apple.cube"));
    }

    [Fact]
    public void A_File_Name_That_Would_Break_The_Filter_Brackets_Is_Not_Listed_Or_Resolved()
    {
        WriteCube(_user, "bad[1].cube");

        Assert.Empty(_library.UserGrades());
        Assert.Null(_library.ResolvePath("user:bad[1].cube"));
    }

    [Fact]
    public void A_Built_In_Grade_Resolves_To_Its_File_And_Missing_Ones_To_Nothing()
    {
        string warm = WriteCube(_builtIn, "warm.cube");

        Assert.Equal(warm, _library.ResolvePath("warm"));
        Assert.Null(_library.ResolvePath("cool"));
        Assert.Null(_library.ResolvePath(VideoLook.NoGrade));
        Assert.Null(_library.ResolvePath("nonsense"));
    }

    [Fact]
    public void A_User_Grade_Resolves_Only_Inside_The_Looks_Folder()
    {
        string mine = WriteCube(_user, "mine.cube");
        WriteCube(_root, "outside.cube");

        Assert.Equal(mine, _library.ResolvePath("user:mine.cube"));
        Assert.Null(_library.ResolvePath(@"user:..\outside.cube"));
        Assert.Null(_library.ResolvePath("user:gone.cube"));
    }

    [Theory]
    [InlineData("LUT_3D_SIZE 33", true)]
    [InlineData("LUT_3D_SIZE 2", true)]
    [InlineData("LUT_3D_SIZE 65", true)]
    [InlineData("LUT_3D_SIZE 1", false)]
    [InlineData("LUT_3D_SIZE 66", false)]
    [InlineData("LUT_3D_SIZE abc", false)]
    [InlineData("LUT_1D_SIZE 1024", false)]
    public void Only_A_Cube_Header_With_A_Size_From_2_To_65_Passes(string header, bool valid)
    {
        string path = WriteCube(_root, "t.cube", header);

        Assert.Equal(valid, LookLibrary.IsValidCube(path));
    }

    [Fact]
    public void Importing_Copies_A_Valid_Cube_And_Renames_On_A_Clash()
    {
        string source = WriteCube(_root, "Sunset.cube");

        Assert.Equal("user:Sunset.cube", _library.TryImport(source));
        Assert.Equal("user:Sunset (2).cube", _library.TryImport(source));
        Assert.Equal("user:Sunset (3).cube", _library.TryImport(source));
        Assert.Equal(3, _library.UserGrades().Count);
    }

    [Fact]
    public void Importing_Refuses_A_File_That_Is_Not_A_3D_Lut()
    {
        string notCube = WriteCube(_root, "other.cube", "LUT_1D_SIZE 16");
        string text = Path.Combine(_root, "readme.txt");
        File.WriteAllText(text, "LUT_3D_SIZE 33");

        Assert.Null(_library.TryImport(notCube));
        Assert.Null(_library.TryImport(text));
        Assert.Null(_library.TryImport(Path.Combine(_root, "missing.cube")));
        Assert.False(Directory.Exists(_user));
    }

    [Fact]
    public void Importing_Swaps_Brackets_In_The_Name_For_Round_Ones()
    {
        string source = WriteCube(_root, "look[2].cube");

        Assert.Equal("user:look(2).cube", _library.TryImport(source));
    }

    [Fact]
    public void Every_Built_In_Grade_Ships_As_A_Valid_33_Point_Cube()
    {
        string folder = Aether.Core.Tests.TestSupport.RepoPath.Get("src", "Aether.App", "Assets", "Looks");

        foreach (string id in LookLibrary.BuiltIn)
        {
            string path = Path.Combine(folder, id + ".cube");
            Assert.True(File.Exists(path), path);
            Assert.True(LookLibrary.IsValidCube(path), path);
            Assert.Contains("LUT_3D_SIZE 33", File.ReadLines(path).Take(3));
        }
    }
}
